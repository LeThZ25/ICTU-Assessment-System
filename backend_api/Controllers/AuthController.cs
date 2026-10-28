using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

        public AuthController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Email))
            {
                return BadRequest(new { Success = false, Message = "Vui lòng nhập Email hoặc Mã sinh viên." });
            }

            string input = dto.Email.Trim().ToLower();
            string email = input;
            string role = "Student";
            string authProvider = "Google";
            string? studentCode = null;
            string fullName = dto.FullName?.Trim() ?? "";

            // Check if user entered bare student code (e.g. DTC205122001)
            if (!input.Contains("@"))
            {
                studentCode = input.ToUpper();
                email = $"{input}@ictu.edu.vn";
                role = "Student";
                authProvider = "Google";
            }
            // Domain 1: Microsoft Office 365 (@ms.ictu.edu.vn)
            else if (input.EndsWith("@ms.ictu.edu.vn"))
            {
                authProvider = "Microsoft";
                role = "Student";
                studentCode = input.Replace("@ms.ictu.edu.vn", "").ToUpper();
            }
            // Domain 2: Google Workspace (@ictu.edu.vn)
            else if (input.EndsWith("@ictu.edu.vn"))
            {
                authProvider = "Google";
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

            // Find or create UserAccount
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                user = new UserAccount
                {
                    Email = email,
                    FullName = string.IsNullOrEmpty(fullName) ? (studentCode != null ? $"Sinh viên {studentCode}" : email) : fullName,
                    Role = role,
                    StudentCode = studentCode,
                    AuthProvider = authProvider,
                    Department = role == "Lecturer" ? "Khoa Công nghệ Thông tin - ICTU" : "Khoa CNTT (Lớp K20)",
                    CreatedAt = DateTime.UtcNow
                };
                _db.Users.Add(user);

                // If student, also ensure Student record exists
                if (studentCode != null)
                {
                    var existingStudent = await _db.Students.FirstOrDefaultAsync(s => s.StudentCode == studentCode);
                    if (existingStudent == null)
                    {
                        _db.Students.Add(new Student
                        {
                            StudentCode = studentCode,
                            FullName = user.FullName,
                            ClassCode = "K20-CNTT"
                        });
                    }
                }
                await _db.SaveChangesAsync();
            }

            // Mock signed token (compact JWT style)
            string token = $"ictu_jwt_{Guid.NewGuid():N}_{user.Role.ToLower()}";

            return Ok(new
            {
                Success = true,
                Token = token,
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
    }

    public class SsoMockDto
    {
        public string Provider { get; set; } = "Google"; // "Google" or "Microsoft"
        public string Role { get; set; } = "Student"; // "Student" or "Lecturer"
    }
}

