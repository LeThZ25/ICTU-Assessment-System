using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace student_desktop_app
{
    public class LoginForm : Form
    {
        private Panel pnlCard = null!;
        private Panel pnlStepRoleSelect = null!;
        private Panel pnlStepStudentLogin = null!;
        private Label lblSubtitle = null!;
        private TextBox txtStudentCode = null!;
        private TextBox txtPassword = null!;
        private Label lblError = null!;
        private Button btnLogin = null!;
        private Button btnBack = null!;

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
                Size = new Size(560, 390),
                Location = new Point((this.ClientSize.Width - 560) / 2, (this.ClientSize.Height - 390) / 2),
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
                Text = "Vui lòng chọn đối tượng đăng nhập",
                ForeColor = Color.FromArgb(161, 161, 170),
                Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point),
                AutoSize = false,
                Size = new Size(pnlCard.Width, 24),
                Location = new Point(0, 114),
                TextAlign = ContentAlignment.MiddleCenter
            };

            pnlCard.Controls.Add(picLogo);
            pnlCard.Controls.Add(lblTitle);
            pnlCard.Controls.Add(lblSubtitle);

            // =================================================================
            // BƯỚC 1: CHỌN ĐỐI TƯỢNG ĐĂNG NHẬP (SINH VIÊN / QUẢN TRỊ)
            // =================================================================
            pnlStepRoleSelect = new Panel
            {
                Size = new Size(490, 215),
                Location = new Point(35, 150),
                BackColor = Color.Transparent
            };

            var cardStudent = CreateRoleCard(
                location: new Point(0, 0),
                isStudentIcon: true,
                title: "Sinh viên",
                subtitle: "Đăng nhập bằng Mã sinh viên (MSV)\nvà mật khẩu (Mặc định: 123456)",
                hoverBorderColor: Color.FromArgb(59, 130, 246),
                onClick: () => ShowStudentCredentialsStep()
            );

            var cardAdmin = CreateRoleCard(
                location: new Point(255, 0),
                isStudentIcon: false,
                title: "Quản trị",
                subtitle: "Đăng nhập hệ thống quản trị\ndành cho Giảng viên / Quản trị",
                hoverBorderColor: Color.FromArgb(249, 115, 22),
                onClick: () =>
                {
                    MessageBox.Show(
                        "Đây là ứng dụng dành riêng cho Sinh viên (Module 2).\nVui lòng chọn đối tượng 'Sinh viên' để đăng nhập bằng Mã sinh viên và mật khẩu!",
                        "Thông báo",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            );

            pnlStepRoleSelect.Controls.Add(cardStudent);
            pnlStepRoleSelect.Controls.Add(cardAdmin);

            // =================================================================
            // BƯỚC 2: FORM NHẬP MÃ SINH VIÊN (MSV) & MẬT KHẨU (MẶC ĐỊNH 123456)
            // =================================================================
            pnlStepStudentLogin = new Panel
            {
                Size = new Size(490, 225),
                Location = new Point(35, 145),
                BackColor = Color.Transparent,
                Visible = false
            };

            var lblCode = new Label
            {
                Text = "Mã sinh viên (MSV):",
                ForeColor = Color.FromArgb(228, 228, 231),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(25, 4),
                AutoSize = true
            };

            txtStudentCode = new TextBox
            {
                Text = "DTC235210069",
                Location = new Point(25, 27),
                Width = 440,
                Height = 32,
                BackColor = Color.FromArgb(28, 28, 34),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Regular)
            };

            var lblPass = new Label
            {
                Text = "Mật khẩu (mặc định: 123456):",
                ForeColor = Color.FromArgb(228, 228, 231),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(25, 68),
                AutoSize = true
            };

            txtPassword = new TextBox
            {
                Text = "123456",
                Location = new Point(25, 91),
                Width = 440,
                Height = 32,
                BackColor = Color.FromArgb(28, 28, 34),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                UseSystemPasswordChar = true,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Regular)
            };

            // Nút chọn nhanh MSV mẫu
            var btnDemo1 = CreateQuickCodeButton("DTC235210069 (Ngọc)", new Point(25, 130), "DTC235210069");
            var btnDemo2 = CreateQuickCodeButton("DTC205122001 (An)", new Point(176, 130), "DTC205122001");
            var btnDemo3 = CreateQuickCodeButton("DTC205122002 (Bình)", new Point(320, 130), "DTC205122002");

            lblError = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(248, 113, 113),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Location = new Point(25, 158),
                Size = new Size(440, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnBack = new Button
            {
                Text = "← Quay lại",
                Location = new Point(25, 182),
                Size = new Size(115, 38),
                BackColor = Color.FromArgb(39, 39, 42),
                ForeColor = Color.FromArgb(212, 212, 216),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnBack.FlatAppearance.BorderColor = Color.FromArgb(63, 63, 70);
            btnBack.Click += (s, e) => ShowRoleSelectionStep();

            btnLogin = new Button
            {
                Text = "Đăng nhập hệ thống",
                Location = new Point(150, 182),
                Size = new Size(315, 38),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.75F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnLogin.FlatAppearance.BorderSize = 0;
            btnLogin.Click += BtnLogin_Click;

            this.AcceptButton = btnLogin;

            pnlStepStudentLogin.Controls.Add(lblCode);
            pnlStepStudentLogin.Controls.Add(txtStudentCode);
            pnlStepStudentLogin.Controls.Add(lblPass);
            pnlStepStudentLogin.Controls.Add(txtPassword);
            pnlStepStudentLogin.Controls.Add(btnDemo1);
            pnlStepStudentLogin.Controls.Add(btnDemo2);
            pnlStepStudentLogin.Controls.Add(btnDemo3);
            pnlStepStudentLogin.Controls.Add(lblError);
            pnlStepStudentLogin.Controls.Add(btnBack);
            pnlStepStudentLogin.Controls.Add(btnLogin);

            pnlCard.Controls.Add(pnlStepRoleSelect);
            pnlCard.Controls.Add(pnlStepStudentLogin);
            this.Controls.Add(pnlCard);
        }

        private Button CreateQuickCodeButton(string label, Point location, string studentCode)
        {
            var btn = new Button
            {
                Text = label,
                Location = location,
                Size = new Size(145, 24),
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
                txtPassword.Text = "123456";
                lblError.Text = "";
            };
            return btn;
        }

        private Panel CreateRoleCard(
            Point location,
            bool isStudentIcon,
            string title,
            string subtitle,
            Color hoverBorderColor,
            Action onClick)
        {
            bool isHovered = false;
            var card = new Panel
            {
                Size = new Size(235, 200),
                Location = location,
                BackColor = Color.FromArgb(24, 24, 28),
                Cursor = Cursors.Hand
            };

            card.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var borderCol = isHovered ? hoverBorderColor : Color.FromArgb(48, 48, 54);
                using var pen = new Pen(borderCol, isHovered ? 1.6f : 1f);
                e.Graphics.DrawRectangle(pen, 1, 1, card.Width - 3, card.Height - 3);

                // Vẽ icon Mũ cử nhân (Sinh viên) hoặc Bánh răng (Quản trị) giống hệt ảnh
                if (isStudentIcon)
                {
                    DrawGraduationCapIcon(e.Graphics, card.Width / 2, 54);
                }
                else
                {
                    DrawGearsIcon(e.Graphics, card.Width / 2, 54);
                }
            };

            var lblRoleTitle = new Label
            {
                Text = title,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12.5F, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(card.Width - 16, 28),
                Location = new Point(8, 96),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };

            var lblRoleDesc = new Label
            {
                Text = subtitle,
                ForeColor = Color.FromArgb(156, 163, 175),
                Font = new Font("Segoe UI", 8.75F, FontStyle.Regular),
                AutoSize = false,
                Size = new Size(card.Width - 20, 56),
                Location = new Point(10, 128),
                TextAlign = ContentAlignment.TopCenter,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };

            void AttachEvents(Control ctrl)
            {
                ctrl.MouseEnter += (s, e) => { isHovered = true; card.Invalidate(); };
                ctrl.MouseLeave += (s, e) => { isHovered = false; card.Invalidate(); };
                ctrl.Click += (s, e) => onClick();
            }

            AttachEvents(card);
            AttachEvents(lblRoleTitle);
            AttachEvents(lblRoleDesc);

            card.Controls.Add(lblRoleTitle);
            card.Controls.Add(lblRoleDesc);
            return card;
        }

        private void DrawGraduationCapIcon(Graphics g, int centerX, int centerY)
        {
            using var brush = new SolidBrush(Color.FromArgb(79, 143, 247)); // #4f8ff7
            using var pen = new Pen(Color.FromArgb(79, 143, 247), 2.5f);

            // Mũ thoi phía trên
            Point[] diamond =
            {
                new Point(centerX, centerY - 18),
                new Point(centerX + 28, centerY - 6),
                new Point(centerX, centerY + 6),
                new Point(centerX - 28, centerY - 6)
            };
            g.FillPolygon(brush, diamond);

            // Phần thân mũ bên dưới
            Point[] capBase =
            {
                new Point(centerX - 16, centerY + 2),
                new Point(centerX + 16, centerY + 2),
                new Point(centerX + 13, centerY + 15),
                new Point(centerX, centerY + 20),
                new Point(centerX - 13, centerY + 15)
            };
            g.FillPolygon(brush, capBase);

            // Tua rua bên trái
            g.DrawLine(pen, centerX - 22, centerY - 3, centerX - 22, centerY + 14);
            g.FillEllipse(brush, centerX - 25, centerY + 13, 6, 6);
        }

        private void DrawGearsIcon(Graphics g, int centerX, int centerY)
        {
            var coral = Color.FromArgb(242, 109, 91); // #f26d5b
            using var penMain = new Pen(coral, 5f);
            using var penSmall = new Pen(coral, 3.5f);
            using var brush = new SolidBrush(coral);

            // Bánh răng lớn
            int gx = centerX - 7;
            int gy = centerY + 2;
            g.DrawEllipse(penMain, gx - 11, gy - 11, 22, 22);
            for (int a = 0; a < 360; a += 45)
            {
                double rad = a * Math.PI / 180.0;
                int x1 = gx + (int)(Math.Cos(rad) * 10);
                int y1 = gy + (int)(Math.Sin(rad) * 10);
                int x2 = gx + (int)(Math.Cos(rad) * 16);
                int y2 = gy + (int)(Math.Sin(rad) * 16);
                g.DrawLine(penMain, x1, y1, x2, y2);
            }

            // Bánh răng nhỏ phía trên phải
            int sx = centerX + 14;
            int sy = centerY - 11;
            g.DrawEllipse(penSmall, sx - 6, sy - 6, 12, 12);
            for (int a = 0; a < 360; a += 60)
            {
                double rad = a * Math.PI / 180.0;
                int x1 = sx + (int)(Math.Cos(rad) * 6);
                int y1 = sy + (int)(Math.Sin(rad) * 6);
                int x2 = sx + (int)(Math.Cos(rad) * 10);
                int y2 = sy + (int)(Math.Sin(rad) * 10);
                g.DrawLine(penSmall, x1, y1, x2, y2);
            }
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
                // Fallback绘制 nếu không tìm thấy file
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

        private void ShowStudentCredentialsStep()
        {
            pnlStepRoleSelect.Visible = false;
            pnlStepStudentLogin.Visible = true;
            lblSubtitle.Text = "Nhập Mã sinh viên (MSV) và mật khẩu (mặc định: 123456)";
            lblError.Text = "";
            txtStudentCode.Focus();
        }

        private void ShowRoleSelectionStep()
        {
            pnlStepStudentLogin.Visible = false;
            pnlStepRoleSelect.Visible = true;
            lblSubtitle.Text = "Vui lòng chọn đối tượng đăng nhập";
            lblError.Text = "";
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

            if (password != "123456")
            {
                lblError.Text = "⚠ Sai mật khẩu! Mật khẩu mặc định của sinh viên là: 123456";
                return;
            }

            string studentCode = rawCode.Contains("@") ? rawCode.Split('@')[0].ToUpper() : rawCode.ToUpper();
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
            string portalUrl = $"{_serverUrl}{separator}studentCode={Uri.EscapeDataString(studentCode)}&studentName={Uri.EscapeDataString(studentName)}&studentEmail={Uri.EscapeDataString(studentEmail)}&loggedIn=1";

            this.Hide();
            var kioskForm = new ExamKioskForm(portalUrl, studentCode, studentName);
            kioskForm.FormClosed += (s, args) => this.Close();
            kioskForm.Show();
        }
    }
}
