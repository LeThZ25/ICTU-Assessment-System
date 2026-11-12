using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace student_desktop_app
{
    public class LoginForm : Form
    {
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private Panel pnlCard = null!;
        private Panel pnlStudentLogin = null!;
        private TextBox txtStudentCode = null!;
        private TextBox txtPassword = null!;
        private Panel btnTogglePassword = null!;
        private bool _isPasswordVisible = false;
        private bool _isLoginBtnHovered = false;
        private string _loginBtnText = "Đăng nhập hệ thống";
        private Label lblError = null!;
        private Panel btnLogin = null!;

        private readonly ApiClient _api;
        private readonly string _serverUrl;
        private readonly bool _useLegacy;

        private readonly Dictionary<string, string> _knownStudents = new(StringComparer.OrdinalIgnoreCase)
        {
            { "DTC235210069", "Phạm Hồng Ngọc Kiên" },
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

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void InitializeComponent()
        {
            this.Text = "Đăng nhập hệ thống";
            this.Size = new Size(780, 540);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(24, 24, 27); // #18181b dark background
            this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            this.DoubleBuffered = true;

            Color cardBg = Color.FromArgb(18, 18, 20);     // #121214
            Color cardBorder = Color.FromArgb(39, 39, 42); // #27272a
            Color inputBg = Color.FromArgb(24, 24, 27);    // #18181b
            Color inputBorder = Color.FromArgb(46, 46, 50);// #2e2e32
            Color accentBlue = Color.FromArgb(79, 143, 247);// #4f8ff7

            pnlCard = new Panel
            {
                Size = new Size(460, 410),
                Location = new Point((this.ClientSize.Width - 460) / 2, (this.ClientSize.Height - 410) / 2),
                BackColor = Color.Transparent
            };
            pnlCard.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, pnlCard.Width - 1, pnlCard.Height - 1);
                using var path = CreateRoundedPath(rect, 16);
                using var brush = new SolidBrush(cardBg);
                using var pen = new Pen(cardBorder, 1f);
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            };
            this.Resize += (s, e) =>
            {
                pnlCard.Location = new Point((this.ClientSize.Width - pnlCard.Width) / 2, (this.ClientSize.Height - pnlCard.Height) / 2);
            };

            // Top ICTU Logo / Icon
            var picLogo = new PictureBox
            {
                Size = new Size(48, 48),
                Location = new Point((pnlCard.Width - 48) / 2, 28),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = cardBg
            };
            TryLoadIctuLogo(picLogo);

            var lblTitle = new Label
            {
                Text = "Đăng nhập hệ thống",
                ForeColor = Color.White,
                BackColor = cardBg,
                Font = new Font("Segoe UI", 18.5F, FontStyle.Bold, GraphicsUnit.Point),
                AutoSize = false,
                Size = new Size(pnlCard.Width - 4, 38),
                Location = new Point(2, 84),
                TextAlign = ContentAlignment.MiddleCenter
            };

            pnlCard.Controls.Add(picLogo);
            pnlCard.Controls.Add(lblTitle);

            // =================================================================
            // FORM NHẬP MÃ SINH VIÊN (MSV) & MẬT KHẨU (MẶC ĐỊNH 123456)
            // =================================================================
            pnlStudentLogin = new Panel
            {
                Size = new Size(400, 265),
                Location = new Point(30, 132),
                BackColor = cardBg,
                Visible = true
            };

            // 1. Nhãn Mã sinh viên (MSV) kèm icon thẻ xanh giống ảnh 1
            var pnlCodeHeader = new Panel
            {
                Location = new Point(2, 0),
                Size = new Size(396, 22),
                BackColor = cardBg
            };
            pnlCodeHeader.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using var iconBrush = new SolidBrush(accentBlue);
                using var darkPen = new Pen(cardBg, 1.3f);
                // Vẽ icon thẻ sinh viên (fa-id-card)
                using (var badgePath = CreateRoundedPath(new Rectangle(1, 4, 15, 11), 2))
                {
                    e.Graphics.FillPath(iconBrush, badgePath);
                }
                e.Graphics.FillEllipse(new SolidBrush(cardBg), 3, 6, 4, 4);
                e.Graphics.DrawLine(darkPen, 9, 7, 14, 7);
                e.Graphics.DrawLine(darkPen, 9, 11, 13, 11);

                using var fLabel = new Font("Segoe UI", 9F, FontStyle.Bold);
                using var textBrush = new SolidBrush(Color.FromArgb(212, 212, 216));
                e.Graphics.DrawString("Mã sinh viên (MSV)", fLabel, textBrush, new PointF(21, 1));
            };

            var pnlCodeWrapper = new Panel
            {
                Location = new Point(2, 24),
                Size = new Size(396, 40),
                BackColor = cardBg,
                Cursor = Cursors.IBeam
            };
            pnlCodeWrapper.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, pnlCodeWrapper.Width - 1, pnlCodeWrapper.Height - 1);
                using var path = CreateRoundedPath(rect, 10);
                using var fill = new SolidBrush(inputBg);
                bool focused = txtStudentCode != null && txtStudentCode.Focused;
                using var pen = new Pen(focused ? accentBlue : inputBorder, focused ? 1.4f : 1f);
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            };
            pnlCodeWrapper.Click += (s, e) => txtStudentCode.Focus();

            txtStudentCode = new TextBox
            {
                Text = "",
                Location = new Point(12, 10),
                Width = 372,
                BackColor = inputBg,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular)
            };
            var lblCodePlaceholder = new Label
            {
                Text = "Nhập MSV",
                Location = new Point(10, 10),
                AutoSize = true,
                BackColor = inputBg,
                ForeColor = Color.FromArgb(113, 113, 122),
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                Cursor = Cursors.IBeam
            };
            lblCodePlaceholder.Click += (s, e) => txtStudentCode.Focus();

            void SyncCodePlaceholder()
            {
                lblCodePlaceholder.Visible = string.IsNullOrEmpty(txtStudentCode.Text) && !txtStudentCode.Focused;
                pnlCodeWrapper.Invalidate();
            }
            txtStudentCode.GotFocus += (s, e) => SyncCodePlaceholder();
            txtStudentCode.LostFocus += (s, e) => SyncCodePlaceholder();
            txtStudentCode.TextChanged += (s, e) => SyncCodePlaceholder();
            txtStudentCode.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    BtnLogin_Click(btnLogin, EventArgs.Empty);
                }
            };
            pnlCodeWrapper.Controls.Add(lblCodePlaceholder);
            pnlCodeWrapper.Controls.Add(txtStudentCode);

            // 2. Nhãn Mật khẩu (icon khóa xanh + Mật khẩu)
            var pnlPassHeader = new Panel
            {
                Location = new Point(2, 78),
                Size = new Size(396, 22),
                BackColor = cardBg
            };
            pnlPassHeader.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using var iconBrush = new SolidBrush(accentBlue);
                using var iconPen = new Pen(accentBlue, 1.6f);
                // Vẽ icon ổ khóa (fa-lock)
                e.Graphics.DrawArc(iconPen, 4, 3, 7, 7, 180, 180);
                using (var lockBody = CreateRoundedPath(new Rectangle(2, 8, 11, 8), 2))
                {
                    e.Graphics.FillPath(iconBrush, lockBody);
                }

                using var fLabel = new Font("Segoe UI", 9F, FontStyle.Bold);
                using var textBrush = new SolidBrush(Color.FromArgb(212, 212, 216));
                e.Graphics.DrawString("Mật khẩu", fLabel, textBrush, new PointF(19, 1));
            };

            // Khung gộp chung ô nhập mật khẩu và biểu tượng con mắt vào cùng 1 ô bo góc liền mạch
            var pnlPasswordWrapper = new Panel
            {
                Location = new Point(2, 102),
                Size = new Size(396, 40),
                BackColor = cardBg,
                Cursor = Cursors.IBeam
            };
            pnlPasswordWrapper.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, pnlPasswordWrapper.Width - 1, pnlPasswordWrapper.Height - 1);
                using var path = CreateRoundedPath(rect, 10);
                using var fill = new SolidBrush(inputBg);
                bool focused = txtPassword != null && txtPassword.Focused;
                using var pen = new Pen(focused ? accentBlue : inputBorder, focused ? 1.4f : 1f);
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            };
            pnlPasswordWrapper.Click += (s, e) => txtPassword.Focus();

            txtPassword = new TextBox
            {
                Text = "",
                Location = new Point(12, 10),
                Width = 342,
                BackColor = inputBg,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                UseSystemPasswordChar = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular)
            };
            var lblPasswordPlaceholder = new Label
            {
                Text = "Nhập mật khẩu",
                Location = new Point(10, 10),
                AutoSize = true,
                BackColor = inputBg,
                ForeColor = Color.FromArgb(113, 113, 122),
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                Cursor = Cursors.IBeam
            };
            lblPasswordPlaceholder.Click += (s, e) => txtPassword.Focus();

            void SyncPasswordPlaceholder()
            {
                lblPasswordPlaceholder.Visible = string.IsNullOrEmpty(txtPassword.Text) && !txtPassword.Focused;
                pnlPasswordWrapper.Invalidate();
            }
            txtPassword.GotFocus += (s, e) => SyncPasswordPlaceholder();
            txtPassword.LostFocus += (s, e) => SyncPasswordPlaceholder();
            txtPassword.TextChanged += (s, e) => SyncPasswordPlaceholder();
            txtPassword.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    BtnLogin_Click(btnLogin, EventArgs.Empty);
                }
            };

            btnTogglePassword = new Panel
            {
                Location = new Point(356, 4),
                Size = new Size(34, 32),
                BackColor = inputBg,
                Cursor = Cursors.Hand
            };
            btnTogglePassword.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var clearBrush = new SolidBrush(inputBg);
                e.Graphics.FillRectangle(clearBrush, btnTogglePassword.ClientRectangle);
                var col = _isPasswordVisible ? accentBlue : Color.FromArgb(161, 161, 170);
                using var pen = new Pen(col, 1.5f);
                using var brush = new SolidBrush(col);
                int cx = btnTogglePassword.Width / 2;
                int cy = btnTogglePassword.Height / 2;

                Point[] eyeOutline =
                {
                    new Point(cx - 9, cy),
                    new Point(cx - 5, cy - 5),
                    new Point(cx, cy - 6),
                    new Point(cx + 5, cy - 5),
                    new Point(cx + 9, cy),
                    new Point(cx + 5, cy + 5),
                    new Point(cx, cy + 6),
                    new Point(cx - 5, cy + 5)
                };
                e.Graphics.DrawPolygon(pen, eyeOutline);
                e.Graphics.FillEllipse(brush, cx - 2, cy - 2, 5, 5);

                if (_isPasswordVisible)
                {
                    e.Graphics.DrawLine(pen, cx - 7, cy + 6, cx + 7, cy - 6);
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

            pnlPasswordWrapper.Controls.Add(lblPasswordPlaceholder);
            pnlPasswordWrapper.Controls.Add(txtPassword);
            pnlPasswordWrapper.Controls.Add(btnTogglePassword);

            // Dòng nhỏ bên dưới ô nhập mật khẩu cho sinh viên quên mật khẩu
            var lblForgotPassword = new Label
            {
                Text = "Quên mật khẩu?",
                ForeColor = accentBlue,
                BackColor = cardBg,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Location = new Point(2, 148),
                Size = new Size(396, 18),
                TextAlign = ContentAlignment.MiddleRight,
                Cursor = Cursors.Hand
            };
            lblForgotPassword.MouseEnter += (s, e) => lblForgotPassword.Font = new Font("Segoe UI", 8.5F, FontStyle.Underline);
            lblForgotPassword.MouseLeave += (s, e) => lblForgotPassword.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular);
            lblForgotPassword.Click += (s, e) => ShowForgotPasswordDialog();

            lblError = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(248, 113, 113),
                BackColor = cardBg,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Location = new Point(2, 172),
                Size = new Size(396, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnLogin = new Panel
            {
                Location = new Point(2, 198),
                Size = new Size(396, 42),
                BackColor = cardBg,
                Cursor = Cursors.Hand
            };
            btnLogin.MouseEnter += (s, e) => { _isLoginBtnHovered = true; btnLogin.Invalidate(); };
            btnLogin.MouseLeave += (s, e) => { _isLoginBtnHovered = false; btnLogin.Invalidate(); };
            btnLogin.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using var clearBrush = new SolidBrush(cardBg);
                e.Graphics.FillRectangle(clearBrush, btnLogin.ClientRectangle);

                var rect = new Rectangle(0, 0, btnLogin.Width - 1, btnLogin.Height - 1);
                using var path = CreateRoundedPath(rect, 11);
                Color btnColor = _isLoginBtnHovered
                    ? Color.FromArgb(29, 78, 216)  // #1d4ed8
                    : Color.FromArgb(37, 99, 235); // #2563eb
                using var fill = new SolidBrush(btnColor);
                e.Graphics.FillPath(fill, path);

                using var btnFont = new Font("Segoe UI", 9.75F, FontStyle.Bold);
                TextRenderer.DrawText(
                    e.Graphics,
                    _loginBtnText,
                    btnFont,
                    btnLogin.ClientRectangle,
                    Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            btnLogin.Click += BtnLogin_Click;

            pnlStudentLogin.Controls.Add(pnlCodeHeader);
            pnlStudentLogin.Controls.Add(pnlCodeWrapper);
            pnlStudentLogin.Controls.Add(pnlPassHeader);
            pnlStudentLogin.Controls.Add(pnlPasswordWrapper);
            pnlStudentLogin.Controls.Add(lblForgotPassword);
            pnlStudentLogin.Controls.Add(lblError);
            pnlStudentLogin.Controls.Add(btnLogin);

            pnlCard.Controls.Add(pnlStudentLogin);
            this.Controls.Add(pnlCard);
            this.Shown += (s, e) => SyncCodePlaceholder();
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
                Text = "Họ và tên sinh viên:",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(22, 118),
                AutoSize = true
            };

            var txtVerifyName = new TextBox
            {
                Text = "",
                PlaceholderText = "Nhập Họ và tên",
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
                lblError.ForeColor = Color.FromArgb(248, 113, 113);
                lblError.Text = "⚠ Sai mật khẩu";
                return;
            }
            string studentName = _knownStudents.TryGetValue(studentCode, out var name) ? name : $"Sinh viên {studentCode}";
            string studentEmail = $"{studentCode.ToLower()}@student.edu.vn";

            btnLogin.Enabled = false;
            _loginBtnText = "Đang đăng nhập...";
            btnLogin.Invalidate();
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
                    _loginBtnText = "Đăng nhập hệ thống";
                    btnLogin.Invalidate();
                }
                return;
            }

            await _api.LoginStudentAsync(studentCode, studentName);

            string separator = _serverUrl.Contains("?") ? "&" : "?";
            string portalUrl = $"{_serverUrl}{separator}studentCode={Uri.EscapeDataString(studentCode)}&studentName={Uri.EscapeDataString(studentName)}&studentEmail={Uri.EscapeDataString(studentEmail)}&syncPwd={Uri.EscapeDataString(expectedPassword)}&loggedIn=1&_t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

            this.Hide();
            var kioskForm = new ExamKioskForm(portalUrl, studentCode, studentName);
            kioskForm.FormClosed += (s, args) => this.Close();
            kioskForm.Show();
        }
    }
}
