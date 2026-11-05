using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace student_desktop_app
{
    public class LoginForm : Form
    {
        private Panel pnlCard = null!;
        private Panel pnlStudentLogin = null!;
        private Label lblSubtitle = null!;
        private TextBox txtStudentCode = null!;
        private TextBox txtPassword = null!;
        private Button btnTogglePassword = null!;
        private bool _isPasswordVisible = false;
        private Label lblError = null!;
        private Button btnLogin = null!;

        private readonly ApiClient _api;
        private readonly string _serverUrl;
        private readonly bool _useLegacy;

        private readonly Dictionary<string, string> _knownStudents = new(StringComparer.OrdinalIgnoreCase)
        {
            { "DTC235210069", "Phạm Hồng Ngọc" },
            { "DTC205122001", "Nguyễn Văn An" },
            { "DTC205122002", "Trần Thị Bình" }
        };

        public LoginForm(
            string serverUrl = "http://localhost:5000/student_web.html?mode=desktop_kiosk",
            bool useLegacy = false)
        {
            _serverUrl = serverUrl;
            _useLegacy = useLegacy;
            _api = new ApiClient();
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Đăng nhập hệ thống";
            this.Size = new Size(760, 520);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(24, 24, 29); // #18181d dark background
            this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

            pnlCard = new Panel
            {
                Size = new Size(490, 415),
                Location = new Point((this.ClientSize.Width - 490) / 2, (this.ClientSize.Height - 415) / 2),
                BackColor = Color.FromArgb(18, 18, 20) // #121214 card
            };
            pnlCard.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(42, 42, 46), 1);
                e.Graphics.DrawRectangle(pen, 0, 0, pnlCard.Width - 1, pnlCard.Height - 1);
            };
            this.Resize += (s, e) =>
            {
                pnlCard.Location = new Point((this.ClientSize.Width - pnlCard.Width) / 2, (this.ClientSize.Height - pnlCard.Height) / 2);
            };

            // Top ICTU Logo / Icon
            var picLogo = new PictureBox
            {
                Size = new Size(44, 44),
                Location = new Point((pnlCard.Width - 44) / 2, 24),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            TryLoadIctuLogo(picLogo);

            var lblTitle = new Label
            {
                Text = "Đăng nhập hệ thống",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Point),
                AutoSize = false,
                Size = new Size(pnlCard.Width, 38),
                Location = new Point(0, 74),
                TextAlign = ContentAlignment.MiddleCenter
            };

            lblSubtitle = new Label
            {
                Text = "Nhập Mã sinh viên (MSV) và mật khẩu (mặc định: 123456)",
                ForeColor = Color.FromArgb(161, 161, 170),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                AutoSize = false,
                Size = new Size(pnlCard.Width, 24),
                Location = new Point(0, 114),
                TextAlign = ContentAlignment.MiddleCenter
            };

            pnlCard.Controls.Add(picLogo);
            pnlCard.Controls.Add(lblTitle);
            pnlCard.Controls.Add(lblSubtitle);

            // =================================================================
            // FORM NHẬP TRỰC TIẾP MÃ SINH VIÊN (MSV) & MẬT KHẨU (MẶC ĐỊNH 123456)
            // =================================================================
            pnlStudentLogin = new Panel
            {
                Size = new Size(440, 255),
                Location = new Point(25, 145),
                BackColor = Color.Transparent,
                Visible = true
            };

            var lblCode = new Label
            {
                Text = "Mã sinh viên (MSV):",
                ForeColor = Color.FromArgb(228, 228, 231),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(15, 4),
                AutoSize = true
            };

            var pnlCodeWrapper = new Panel
            {
                Location = new Point(15, 27),
                Size = new Size(410, 29),
                BackColor = Color.FromArgb(28, 28, 34),
                Cursor = Cursors.IBeam
            };
            pnlCodeWrapper.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(100, 100, 110), 1);
                e.Graphics.DrawRectangle(pen, 0, 0, pnlCodeWrapper.Width - 1, pnlCodeWrapper.Height - 1);
            };
            pnlCodeWrapper.Click += (s, e) => txtStudentCode.Focus();

            txtStudentCode = new TextBox
            {
                Text = "DTC235210069",
                Location = new Point(6, 5),
                Width = 398,
                BackColor = Color.FromArgb(28, 28, 34),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Regular)
            };
            pnlCodeWrapper.Controls.Add(txtStudentCode);

            var lblPass = new Label
            {
                Text = "Mật khẩu (mặc định: 123456):",
                ForeColor = Color.FromArgb(228, 228, 231),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(15, 68),
                AutoSize = true
            };

            // Khung gộp chung ô nhập mật khẩu và biểu tượng con mắt vào cùng 1 ô liền mạch
            var pnlPasswordWrapper = new Panel
            {
                Location = new Point(15, 91),
                Size = new Size(410, 29),
                BackColor = Color.FromArgb(28, 28, 34)
            };
            pnlPasswordWrapper.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(100, 100, 110), 1);
                e.Graphics.DrawRectangle(pen, 0, 0, pnlPasswordWrapper.Width - 1, pnlPasswordWrapper.Height - 1);
            };

            txtPassword = new TextBox
            {
                Text = "123456",
                Location = new Point(6, 5),
                Width = 362,
                BackColor = Color.FromArgb(28, 28, 34),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                UseSystemPasswordChar = true,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Regular)
            };

            btnTogglePassword = new Button
            {
                Text = "",
                Location = new Point(372, 1),
                Size = new Size(37, 27),
                BackColor = Color.FromArgb(28, 28, 34),
                ForeColor = Color.FromArgb(161, 161, 170),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnTogglePassword.FlatAppearance.BorderSize = 0;
            btnTogglePassword.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 28, 34);
            btnTogglePassword.FlatAppearance.MouseDownBackColor = Color.FromArgb(28, 28, 34);
            btnTogglePassword.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var col = _isPasswordVisible ? Color.FromArgb(79, 143, 247) : Color.FromArgb(161, 161, 170);
                using var pen = new Pen(col, 1.6f);
                using var brush = new SolidBrush(col);
                int cx = btnTogglePassword.Width / 2;
                int cy = btnTogglePassword.Height / 2;

                // Vẽ viền con mắt
                Point[] eyeOutline =
                {
                    new Point(cx - 10, cy),
                    new Point(cx - 5, cy - 5),
                    new Point(cx, cy - 6),
                    new Point(cx + 5, cy - 5),
                    new Point(cx + 10, cy),
                    new Point(cx + 5, cy + 5),
                    new Point(cx, cy + 6),
                    new Point(cx - 5, cy + 5)
                };
                e.Graphics.DrawPolygon(pen, eyeOutline);
                // Con ngươi
                e.Graphics.FillEllipse(brush, cx - 3, cy - 3, 6, 6);

                // Đường gạch chéo khi đang hiện mật khẩu (nhấn để ẩn lại)
                if (_isPasswordVisible)
                {
                    e.Graphics.DrawLine(pen, cx - 8, cy + 7, cx + 8, cy - 7);
                }
            };
            btnTogglePassword.Click += (s, e) =>
            {
                _isPasswordVisible = !_isPasswordVisible;
                txtPassword.UseSystemPasswordChar = !_isPasswordVisible;
                btnTogglePassword.Invalidate();
                txtPassword.Focus();
                txtPassword.SelectionStart = txtPassword.Text.Length;
            };

            pnlPasswordWrapper.Controls.Add(txtPassword);
            pnlPasswordWrapper.Controls.Add(btnTogglePassword);

            // Dòng nhỏ bên dưới ô nhập mật khẩu cho sinh viên quên mật khẩu bấm vào lấy lại mật khẩu ngẫu nhiên
            var lblForgotPassword = new Label
            {
                Text = "Quên mật khẩu? Bấm vào đây để lấy lại mật khẩu",
                ForeColor = Color.FromArgb(79, 143, 247),
                Font = new Font("Segoe UI", 8.25F, FontStyle.Underline),
                Location = new Point(15, 123),
                Size = new Size(410, 18),
                TextAlign = ContentAlignment.MiddleRight,
                Cursor = Cursors.Hand
            };
            lblForgotPassword.Click += (s, e) => ShowForgotPasswordDialog();

            // Nút chọn nhanh MSV mẫu
            var btnDemo1 = CreateQuickCodeButton("DTC235210069 (Ngọc)", new Point(15, 146), "DTC235210069");
            var btnDemo2 = CreateQuickCodeButton("DTC205122001 (An)", new Point(156, 146), "DTC205122001");
            var btnDemo3 = CreateQuickCodeButton("DTC205122002 (Bình)", new Point(290, 146), "DTC205122002");

            lblError = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(248, 113, 113),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Location = new Point(15, 174),
                Size = new Size(410, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnLogin = new Button
            {
                Text = "Đăng nhập hệ thống",
                Location = new Point(15, 198),
                Size = new Size(410, 40),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnLogin.FlatAppearance.BorderSize = 0;
            btnLogin.Click += BtnLogin_Click;

            this.AcceptButton = btnLogin;

            pnlStudentLogin.Controls.Add(lblCode);
            pnlStudentLogin.Controls.Add(pnlCodeWrapper);
            pnlStudentLogin.Controls.Add(lblPass);
            pnlStudentLogin.Controls.Add(pnlPasswordWrapper);
            pnlStudentLogin.Controls.Add(lblForgotPassword);
            pnlStudentLogin.Controls.Add(btnDemo1);
            pnlStudentLogin.Controls.Add(btnDemo2);
            pnlStudentLogin.Controls.Add(btnDemo3);
            pnlStudentLogin.Controls.Add(lblError);
            pnlStudentLogin.Controls.Add(btnLogin);

            pnlCard.Controls.Add(pnlStudentLogin);
            this.Controls.Add(pnlCard);
        }

        private Button CreateQuickCodeButton(string label, Point location, string studentCode)
        {
            var btn = new Button
            {
                Text = label,
                Location = location,
                Size = new Size(135, 24),
                BackColor = Color.FromArgb(24, 24, 29),
                ForeColor = Color.FromArgb(161, 161, 170),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.75F, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(46, 46, 52);
            btn.Click += (s, e) =>
            {
                txtStudentCode.Text = studentCode;
                txtPassword.Text = GetExpectedPassword(studentCode);
                lblError.Text = "";
            };
            return btn;
        }

        private static string GetExpectedPassword(string studentCode)
        {
            try
            {
                string filePath = Path.Combine(Path.GetTempPath(), "ICTU_Student_Passwords.json");
                if (File.Exists(filePath))
                {
                    string raw = File.ReadAllText(filePath);
                    var map = JsonSerializer.Deserialize<Dictionary<string, string>>(raw);
                    if (map != null && map.TryGetValue(studentCode.Trim().ToUpperInvariant(), out var savedPwd) && !string.IsNullOrEmpty(savedPwd))
                    {
                        return savedPwd;
                    }
                }
            }
            catch { }
            return "123456";
        }

        private static void SaveExpectedPassword(string studentCode, string newPassword)
        {
            try
            {
                string filePath = Path.Combine(Path.GetTempPath(), "ICTU_Student_Passwords.json");
                Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(filePath))
                {
                    string raw = File.ReadAllText(filePath);
                    var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(raw);
                    if (existing != null)
                    {
                        foreach (var kv in existing) map[kv.Key] = kv.Value;
                    }
                }
                map[studentCode.Trim().ToUpperInvariant()] = newPassword.Trim();
                File.WriteAllText(filePath, JsonSerializer.Serialize(map));
            }
            catch { }
        }

        private static string GenerateRandomNonDefaultPassword(string previousPwd)
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var rng = new Random();
            string result;
            do
            {
                var buffer = new char[6];
                for (int i = 0; i < buffer.Length; i++)
                {
                    buffer[i] = chars[rng.Next(chars.Length)];
                }
                result = new string(buffer);
            } while (result == "123456" || string.Equals(result, previousPwd, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private void ShowForgotPasswordDialog()
        {
            using var dlg = new Form
            {
                Text = "Khôi phục mật khẩu sinh viên",
                Size = new Size(440, 310),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(18, 18, 20),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.25F)
            };

            var lblInfo = new Label
            {
                Text = "Xác minh Mã sinh viên (MSV) và Họ tên để cấp lại mật khẩu ngẫu nhiên mới (không trùng mật khẩu mặc định 123456):",
                ForeColor = Color.FromArgb(212, 212, 216),
                Location = new Point(22, 16),
                Size = new Size(380, 38)
            };

            var lblCode = new Label
            {
                Text = "Mã sinh viên (MSV):",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(22, 60),
                AutoSize = true
            };

            var txtVerifyCode = new TextBox
            {
                Text = txtStudentCode.Text.Trim(),
                Location = new Point(22, 82),
                Width = 380,
                BackColor = Color.FromArgb(28, 28, 34),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblName = new Label
            {
                Text = "Họ và tên sinh viên (Xác minh danh tính):",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(22, 118),
                AutoSize = true
            };

            var txtVerifyName = new TextBox
            {
                Text = "",
                PlaceholderText = "Nhập Họ và tên (VD: Phạm Hồng Ngọc)",
                Location = new Point(22, 140),
                Width = 380,
                BackColor = Color.FromArgb(28, 28, 34),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblResult = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(52, 211, 153),
                Font = new Font("Segoe UI", 8.75F, FontStyle.Bold),
                Location = new Point(22, 174),
                Size = new Size(380, 38)
            };

            var btnVerify = new Button
            {
                Text = "Xác minh && Cấp mật khẩu ngẫu nhiên",
                Location = new Point(22, 218),
                Size = new Size(380, 36),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnVerify.FlatAppearance.BorderSize = 0;

            btnVerify.Click += (s, e) =>
            {
                string code = txtVerifyCode.Text.Trim().ToUpperInvariant();
                string nameInput = txtVerifyName.Text.Trim();

                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(nameInput))
                {
                    lblResult.ForeColor = Color.FromArgb(248, 113, 113);
                    lblResult.Text = "⚠ Vui lòng nhập đầy đủ Mã sinh viên và Họ tên để xác minh!";
                    return;
                }

                if (_knownStudents.TryGetValue(code, out var expectedName))
                {
                    if (nameInput.Length < 2 || (!expectedName.Contains(nameInput, StringComparison.OrdinalIgnoreCase) && !nameInput.Contains(expectedName, StringComparison.OrdinalIgnoreCase)))
                    {
                        lblResult.ForeColor = Color.FromArgb(248, 113, 113);
                        lblResult.Text = $"⚠ Họ tên xác minh không khớp với {code} (Đúng: {expectedName})!";
                        return;
                    }
                }

                string oldPwd = GetExpectedPassword(code);
                string newRandomPwd = GenerateRandomNonDefaultPassword(oldPwd);
                SaveExpectedPassword(code, newRandomPwd);

                txtStudentCode.Text = code;
                txtPassword.Text = newRandomPwd;
                lblError.ForeColor = Color.FromArgb(52, 211, 153);
                lblError.Text = $"✓ Mật khẩu mới của {code}: {newRandomPwd} (Đã tự động điền)";

                MessageBox.Show(
                    $"Xác minh danh tính thành công!\n\nMã sinh viên: {code}\nMật khẩu ngẫu nhiên mới của bạn là: {newRandomPwd}\n\n(Hệ thống đã tự động điền mật khẩu mới vào ô đăng nhập cho bạn)",
                    "Cấp lại mật khẩu thành công",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                dlg.Close();
            };

            dlg.Controls.Add(lblInfo);
            dlg.Controls.Add(lblCode);
            dlg.Controls.Add(txtVerifyCode);
            dlg.Controls.Add(lblName);
            dlg.Controls.Add(txtVerifyName);
            dlg.Controls.Add(lblResult);
            dlg.Controls.Add(btnVerify);
            dlg.ShowDialog(this);
        }

        private void TryLoadIctuLogo(PictureBox pic)
        {
            try
            {
                string[] candidatePaths =
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "backend_api", "wwwroot", "images", "ictu_logo.png"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "images", "ictu_logo.png"),
                    @"c:\Users\APlaptop\source\repos\ICTU-Assessment-System\backend_api\wwwroot\images\ictu_logo.png"
                };

                foreach (var p in candidatePaths)
                {
                    if (File.Exists(p))
                    {
                        pic.Image = Image.FromFile(p);
                        return;
                    }
                }
            }
            catch
            {
            }

            pic.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var brush = new SolidBrush(Color.FromArgb(0, 102, 204));
                e.Graphics.FillEllipse(brush, 2, 2, 38, 38);
                using var font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
                e.Graphics.DrawString("ICTU", font, Brushes.White, new PointF(6, 12));
            };
        }

        private async void BtnLogin_Click(object? sender, EventArgs e)
        {
            var rawCode = txtStudentCode.Text.Trim();
            var password = txtPassword.Text.Trim();

            if (string.IsNullOrEmpty(rawCode))
            {
                lblError.Text = "⚠ Vui lòng nhập Mã sinh viên (MSV)!";
                return;
            }

            string studentCode = rawCode.Contains("@") ? rawCode.Split('@')[0].ToUpper() : rawCode.ToUpper();
            string expectedPassword = GetExpectedPassword(studentCode);

            if (password != expectedPassword)
            {
                lblError.Text = expectedPassword == "123456"
                    ? "⚠ Sai mật khẩu! Mật khẩu mặc định của sinh viên là: 123456"
                    : "⚠ Sai mật khẩu! Vui lòng nhập đúng mật khẩu mới bạn đã đổi.";
                return;
            }
            string studentName = _knownStudents.TryGetValue(studentCode, out var name) ? name : $"Sinh viên {studentCode}";
            string studentEmail = $"{studentCode.ToLower()}@student.edu.vn";

            btnLogin.Enabled = false;
            btnLogin.Text = "Đang đăng nhập...";
            lblError.ForeColor = Color.FromArgb(96, 165, 250);
            lblError.Text = "Đang xác thực tài khoản sinh viên và mở cổng học tập...";

            if (_useLegacy)
            {
                var startRes = await _api.StartExamAsync(1, studentCode, studentName, "ICTU2026");
                if (startRes != null && startRes.Success)
                {
                    this.Hide();
                    var examForm = new ExamForm(_api, startRes, studentCode, studentName);
                    examForm.FormClosed += (s, args) => this.Close();
                    examForm.Show();
                }
                else
                {
                    lblError.ForeColor = Color.FromArgb(248, 113, 113);
                    lblError.Text = "Không thể kết nối máy chủ khảo thí.";
                    btnLogin.Enabled = true;
                    btnLogin.Text = "Đăng nhập hệ thống";
                }
                return;
            }

            await _api.LoginStudentAsync(studentCode, studentName);

            string separator = _serverUrl.Contains("?") ? "&" : "?";
            string portalUrl = $"{_serverUrl}{separator}studentCode={Uri.EscapeDataString(studentCode)}&studentName={Uri.EscapeDataString(studentName)}&studentEmail={Uri.EscapeDataString(studentEmail)}&syncPwd={Uri.EscapeDataString(expectedPassword)}&loggedIn=1";

            this.Hide();
            var kioskForm = new ExamKioskForm(portalUrl, studentCode, studentName);
            kioskForm.FormClosed += (s, args) => this.Close();
            kioskForm.Show();
        }
    }
}
