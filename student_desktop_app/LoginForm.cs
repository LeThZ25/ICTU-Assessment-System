using System;
using System.Drawing;
using System.Windows.Forms;

namespace student_desktop_app
{
    public class LoginForm : Form
    {
        private TextBox txtEmailOrCode = null!;
        private TextBox txtStudentName = null!;
        private TextBox txtAccessCode = null!;
        private Label lblProviderDetect = null!;
        private Button btnLogin = null!;
        private Button btnQuickStudent1 = null!;
        private Button btnQuickStudent2 = null!;
        private Button btnQuickStudent3 = null!;
        private Label lblStatus = null!;

        private readonly ApiClient _api;
        private readonly string _serverUrl;
        private readonly bool _useLegacy;

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
            this.Text = "Đăng Nhập Cổng Học Tập & Khảo Thí Sinh Viên";
            this.Size = new Size(560, 620);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = Color.FromArgb(0, 102, 204)
            };

            var pnlAccentLine = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 4,
                BackColor = Color.FromArgb(59, 130, 246)
            };
            pnlHeader.Controls.Add(pnlAccentLine);

            var lblLogo = new Label
            {
                Text = "CỔNG HỌC TẬP & KHẢO THÍ SINH VIÊN",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(28, 18),
                AutoSize = true
            };

            var lblSub = new Label
            {
                Text = "Hệ thống Quản lý Tiến độ Học tập & Đánh giá Năng lực Thông minh",
                ForeColor = Color.FromArgb(224, 242, 254),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                Location = new Point(28, 50),
                AutoSize = true
            };

            pnlHeader.Controls.Add(lblLogo);
            pnlHeader.Controls.Add(lblSub);

            // Quick account selection
            var lblSso = new Label
            {
                Text = "Chọn nhanh tài khoản sinh viên:",
                Location = new Point(45, 110),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 65, 85)
            };

            btnQuickStudent1 = new Button
            {
                Text = "Phạm Hồng Ngọc (DTC235210069)",
                Location = new Point(45, 136),
                Width = 450,
                Height = 36,
                BackColor = Color.FromArgb(239, 246, 255),
                ForeColor = Color.FromArgb(0, 102, 204),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                Font = new Font("Segoe UI", 8.75F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnQuickStudent1.FlatAppearance.BorderColor = Color.FromArgb(191, 219, 254);
            btnQuickStudent1.Click += (s, e) =>
            {
                txtEmailOrCode.Text = "dtc235210069@student.edu.vn";
                txtStudentName.Text = "Phạm Hồng Ngọc";
            };

            btnQuickStudent2 = new Button
            {
                Text = "Nguyễn Văn An (DTC205122001)",
                Location = new Point(45, 178),
                Width = 220,
                Height = 36,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(16, 185, 129),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnQuickStudent2.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
            btnQuickStudent2.Click += (s, e) =>
            {
                txtEmailOrCode.Text = "dtc205122001@student.edu.vn";
                txtStudentName.Text = "Nguyễn Văn An";
            };

            btnQuickStudent3 = new Button
            {
                Text = "Trần Thị Bình (DTC205122002)",
                Location = new Point(275, 178),
                Width = 220,
                Height = 36,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(2, 132, 199),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnQuickStudent3.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
            btnQuickStudent3.Click += (s, e) =>
            {
                txtEmailOrCode.Text = "dtc205122002@student.edu.vn";
                txtStudentName.Text = "Trần Thị Bình";
            };

            // Form inputs
            var lblCode = new Label
            {
                Text = "Email Sinh Viên / Mã Sinh Viên (MSV):",
                Location = new Point(45, 230),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            txtEmailOrCode = new TextBox
            {
                Text = "dtc235210069@student.edu.vn",
                Location = new Point(45, 255),
                Width = 450,
                Height = 32
            };
            txtEmailOrCode.TextChanged += TxtEmailOrCode_TextChanged;

            lblProviderDetect = new Label
            {
                Text = "✓ Đã xác thực tài khoản sinh viên chính quy",
                ForeColor = Color.FromArgb(16, 185, 129),
                Location = new Point(45, 288),
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold)
            };

            var lblName = new Label
            {
                Text = "Họ và Tên Sinh Viên:",
                Location = new Point(45, 316),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            txtStudentName = new TextBox
            {
                Text = "Phạm Hồng Ngọc",
                Location = new Point(45, 340),
                Width = 450,
                Height = 32
            };

            var lblAccess = new Label
            {
                Text = "Mã Phòng Thi Mặc Định (khi vào mục Kiểm tra / Thi):",
                Location = new Point(45, 382),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            txtAccessCode = new TextBox
            {
                Text = "ICTU2026",
                Location = new Point(45, 406),
                Width = 450,
                Height = 32,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            lblStatus = new Label
            {
                Text = "Chế độ bảo mật: Cửa sổ thường khi xem lịch học/bảng tin; tự động bật Fullscreen & Anti-Cheat khi vào Kiểm tra / Thi.",
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(45, 448),
                Width = 450,
                Height = 36,
                Font = new Font("Segoe UI", 8F)
            };

            btnLogin = new Button
            {
                Text = "ĐĂNG NHẬP HỆ THỐNG HỌC TẬP & KHẢO THÍ",
                Location = new Point(45, 495),
                Width = 450,
                Height = 48,
                BackColor = Color.FromArgb(0, 102, 204),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnLogin.FlatAppearance.BorderSize = 0;
            btnLogin.Click += BtnLogin_Click;

            this.Controls.Add(pnlHeader);
            this.Controls.Add(lblSso);
            this.Controls.Add(btnQuickStudent1);
            this.Controls.Add(btnQuickStudent2);
            this.Controls.Add(btnQuickStudent3);
            this.Controls.Add(lblCode);
            this.Controls.Add(txtEmailOrCode);
            this.Controls.Add(lblProviderDetect);
            this.Controls.Add(lblName);
            this.Controls.Add(txtStudentName);
            this.Controls.Add(lblAccess);
            this.Controls.Add(txtAccessCode);
            this.Controls.Add(lblStatus);
            this.Controls.Add(btnLogin);
        }

        private void TxtEmailOrCode_TextChanged(object? sender, EventArgs e)
        {
            var text = txtEmailOrCode.Text.Trim().ToLower();
            if (text.Contains("@"))
            {
                lblProviderDetect.Text = "✓ Đã nhận diện tài khoản Email sinh viên";
                lblProviderDetect.ForeColor = Color.FromArgb(16, 185, 129);
            }
            else if (text.Length >= 5)
            {
                lblProviderDetect.Text = "✓ Đã nhận diện Mã sinh viên chính quy";
                lblProviderDetect.ForeColor = Color.FromArgb(0, 120, 212);
            }
            else
            {
                lblProviderDetect.Text = "• Nhập mã SV hoặc email sinh viên";
                lblProviderDetect.ForeColor = Color.FromArgb(100, 116, 139);
            }
        }

        private async void BtnLogin_Click(object? sender, EventArgs e)
        {
            var input = txtEmailOrCode.Text.Trim();
            var studentName = txtStudentName.Text.Trim();
            var accessCode = txtAccessCode.Text.Trim();

            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(studentName))
            {
                MessageBox.Show("Vui lòng nhập đầy đủ Email/Mã SV và Họ tên sinh viên.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string studentCode = input.Contains("@") ? input.Split('@')[0].ToUpper() : input.ToUpper();
            string studentEmail = input.Contains("@") ? input.ToLower() : $"{studentCode.ToLower()}@student.edu.vn";

            btnLogin.Enabled = false;
            lblStatus.Text = "Đang xác thực tài khoản và tải dữ liệu cổng học tập sinh viên...";
            lblStatus.ForeColor = Color.FromArgb(0, 102, 204);

            if (_useLegacy)
            {
                var startRes = await _api.StartExamAsync(1, studentCode, studentName, accessCode);
                if (startRes != null && startRes.Success)
                {
                    this.Hide();
                    var examForm = new ExamForm(_api, startRes, studentCode, studentName);
                    examForm.FormClosed += (s, args) => this.Close();
                    examForm.Show();
                }
                else
                {
                    lblStatus.Text = "Không thể vào phòng thi. Kiểm tra máy chủ hoặc mã phòng thi.";
                    lblStatus.ForeColor = Color.FromArgb(225, 29, 72);
                    btnLogin.Enabled = true;
                }
                return;
            }

            // Đăng nhập tài khoản sinh viên với Backend API (nếu Backend đang chạy)
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
