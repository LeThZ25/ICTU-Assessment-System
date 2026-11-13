using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend_api.Data;
using backend_api.Models;

namespace backend_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _environment;

        public AuthController(ApplicationDbContext db, IWebHostEnvironment environment)
        {
            _db = db;
            _environment = environment;
        }

        private bool IsLocalDevelopment => _environment.IsDevelopment() &&
            (HttpContext.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip));

        [Authorize]
        [HttpGet("session")]
        public IActionResult Session() => Ok(new { success = true, user = new {
            email = User.FindFirstValue(ClaimTypes.Email),
            fullName = User.FindFirstValue(ClaimTypes.Name),
            role = User.FindFirstValue(ClaimTypes.Role),
            studentCode = User.FindFirstValue("StudentCode")
        }});

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new { success = true });
        }

        /// <summary>Only provisioned users may sign in through a real identity provider.</summary>
        [HttpGet("sso/{provider}")]
        public IActionResult StartSso(string provider, [FromServices] IConfiguration configuration)
        {
            var scheme = provider.ToLowerInvariant() switch
            {
                "google" => "Google",
                "microsoft" => "Microsoft",
                _ => null
            };
            if (scheme == null) return NotFound();
            var section = configuration.GetSection($"Authentication:{scheme}");
            if (string.IsNullOrWhiteSpace(section["ClientId"]) ||
                string.IsNullOrWhiteSpace(section["ClientSecret"]) ||
                (scheme == "Google" && !Regex.IsMatch(section["ClientId"]!,
                    @"^\d+-[a-z0-9]+\.apps\.googleusercontent\.com$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) ||
                (scheme == "Microsoft" && (!Guid.TryParse(section["TenantId"], out _) ||
                    !Guid.TryParse(section["ClientId"], out _))))
                return StatusCode(503, new { success = false, code = "SSO_NOT_CONFIGURED",
                    message = "Quản trị viên chưa cấu hình thông tin SSO cho cổng đăng nhập." });

            // Fixed destinations: do not accept arbitrary return URLs from a request.
            var destination = scheme == "Google" ? "/index.html" : "/student_web.html";
            if (scheme == "Google" && Request.Query["portal"] == "student")
                destination = "/student_web.html";
            return Challenge(new AuthenticationProperties { RedirectUri = destination }, scheme);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
        {
            // Email-only login is a development demo, never a production identity check.
            if (!IsLocalDevelopment)
                return StatusCode(403, new { success = false, message = "Đăng nhập demo chỉ khả dụng khi chạy Development trên localhost. Cần cấu hình SSO thực cho môi trường triển khai." });
            if (dto == null)
                return BadRequest(new { success = false, message = "Thiếu dữ liệu đăng nhập." });
            if (string.IsNullOrWhiteSpace(dto.Email))
            {
                return BadRequest(new { Success = false, Message = "Vui lòng nhập Email hoặc Mã sinh viên." });
            }

            string input = dto.Email.Trim().ToLower();
            string email = input;
            string role = "Student";
            string? studentCode = null;
            string fullName = dto.FullName?.Trim() ?? "";

            // Check if user entered bare student code (e.g. DTC205122001)
            if (!input.Contains("@"))
            {
                studentCode = input.ToUpper();
                email = $"{input}@ictu.edu.vn";
                role = "Student";
            }
            // Domain 1: Microsoft Office 365 (@ms.ictu.edu.vn)
            else if (input.EndsWith("@ms.ictu.edu.vn"))
            {
                role = "Student";
                studentCode = input.Replace("@ms.ictu.edu.vn", "").ToUpper();
            }
            // Domain 2: Google Workspace (@ictu.edu.vn)
            else if (input.EndsWith("@ictu.edu.vn"))
            {
                string prefix = input.Replace("@ictu.edu.vn", "");

                // Lecturer pattern detection: begins with "gv" or doesn't have numeric student ID
                if (prefix.StartsWith("gv") || prefix.StartsWith("giangvien") || prefix.StartsWith("thay") || prefix.StartsWith("co") || !Regex.IsMatch(prefix, @"\d{3,}"))
                {
                    role = "Lecturer";
                    studentCode = null;
                    if (string.IsNullOrEmpty(fullName)) fullName = $"Giảng viên {prefix}";
                }
                else
                {
                    role = "Student";
                    studentCode = prefix.ToUpper();
                }
            }
            else
            {
                return BadRequest(new
                {
                    Success = false,
                    Message = "Email không thuộc hệ thống ICTU! Vui lòng sử dụng @ictu.edu.vn (Google) hoặc @ms.ictu.edu.vn (Microsoft)."
                });
            }

            // Strict filter if caller requires Lecturer role
            if (dto.RequireLecturer)
            {
                if (!input.EndsWith("@ictu.edu.vn"))
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Message = "Truy cập bị từ chối! Cổng Giảng viên chỉ chấp nhận tài khoản Google của cán bộ nhà trường có định dạng @ictu.edu.vn."
                    });
                }

                if (role != "Lecturer")
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Message = $"Truy cập bị từ chối! Email '{input}' là tài khoản sinh viên (DTC), không có quyền truy cập Cổng Khảo thí Giảng viên ICTU."
                    });
                }
            }

            // Demo accounts must be provisioned in advance. An arbitrary email cannot
            // create a student identity and gain access to assigned assessments.
            var emailKey = email.Trim().ToUpperInvariant();
            var matchingUsers = await _db.Users
                .Where(u => u.Email.Trim().ToUpper() == emailKey)
                .Take(2).ToListAsync();
            // Duplicate identities differing only by case must never select an arbitrary role.
            if (matchingUsers.Count != 1)
                return StatusCode(403, new { success = false, message = "Tài khoản chưa được cấp quyền trong hệ thống." });
            var user = matchingUsers[0];
            if (user.Role != role)
                return StatusCode(403, new { success = false, message = "Vai trò tài khoản không khớp với cổng đăng nhập." });
            if (role == "Student" &&
                (string.IsNullOrWhiteSpace(user.StudentCode) ||
                 !string.Equals(user.StudentCode.Trim(), studentCode, StringComparison.OrdinalIgnoreCase)))
                return StatusCode(403, new { success = false, message = "Định danh sinh viên không khớp với hồ sơ tài khoản." });
            // Do not grant lecturer privileges from an email pattern alone.
            if (role == "Lecturer" && user.Role != "Lecturer")
                return StatusCode(403, new { success = false, message = "Tài khoản giảng viên demo chưa được cấp quyền trong hệ thống." });
            if (dto.RequireLecturer && user.Role != "Lecturer")
                return Forbid();
            if (role == "Student" && !await _db.Students.AnyAsync(s =>
                    s.StudentCode.Trim().ToUpper() == studentCode))
                return StatusCode(403, new { success = false, message = "Sinh viên chưa có hồ sơ được cấp phép." });

            var claims = new System.Collections.Generic.List<Claim> {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Role, user.Role)
            };
            if (user.Role == "Student" && !string.IsNullOrWhiteSpace(user.StudentCode))
                claims.Add(new Claim("StudentCode", user.StudentCode.Trim().ToUpperInvariant()));
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

            return Ok(new
            {
                Success = true,
                User = new
                {
                    user.Id,
                    user.Email,
                    user.FullName,
                    user.Role,
                    user.StudentCode,
                    user.AuthProvider,
                    user.Department
                }
            });
        }

        [HttpPost("sso-mock")]
        public async Task<IActionResult> SsoMock([FromBody] SsoMockDto dto)
        {
            if (!IsLocalDevelopment) return NotFound();
            if (dto == null || string.IsNullOrWhiteSpace(dto.Provider))
            {
                return BadRequest(new { success = false, message = "Vui lòng chọn cổng đăng nhập (Google hoặc Microsoft)." });
            }

            string email;
            string name;
            if (dto.Provider == "Microsoft")
            {
                email = "dtc205122002@ms.ictu.edu.vn";
                name = "Trần Thị Bình (Microsoft 365 ICTU)";
            }
            else if (dto.Role == "Lecturer")
            {
                email = "gv_nguyenvana@ictu.edu.vn";
                name = "TS. Nguyễn Văn A (Giảng viên ICTU)";
            }
            else
            {
                email = "dtc205122001@ictu.edu.vn";
                name = "Nguyễn Văn An (Google Workspace ICTU)";
            }

            return await Login(new LoginRequestDto
            {
                Email = email,
                FullName = name,
                Provider = dto.Provider
            });
        }

        [HttpGet("sample-accounts")]
        public IActionResult GetSampleAccounts()
        {
            if (!IsLocalDevelopment) return NotFound();
            return Ok(new[]
            {
                new { Type = "Sinh viên (Google)", Email = "dtc205122001@ictu.edu.vn", Provider = "Google Workspace", Role = "Student" },
                new { Type = "Sinh viên (Microsoft)", Email = "dtc205122002@ms.ictu.edu.vn", Provider = "Microsoft 365", Role = "Student" },
                new { Type = "Giảng viên (Khoa CNTT)", Email = "gv_nguyenvana@ictu.edu.vn", Provider = "Google/Email ICTU", Role = "Lecturer" }
            });
        }
    }

    public class LoginRequestDto
    {
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string? Password { get; set; }
        public string? Provider { get; set; }
        public bool RequireLecturer { get; set; } = false;
    }

    public class SsoMockDto
    {
        public string Provider { get; set; } = "Google"; // "Google" or "Microsoft"
        public string Role { get; set; } = "Student"; // "Student" or "Lecturer"
    }
}

