using System;
using System.Drawing;
using System.Windows.Forms;

namespace student_desktop_app
{
    public class ResultForm : Form
    {
        public ResultForm(SubmitExamResponse result, int violations)
        {
            this.Text = "Kết Quả Đánh Giá Năng Lực Học Phần - ICTU";
            this.Size = new Size(580, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 160,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(20)
            };

            var lblTitle = new Label
            {
                Text = "HOÀN THÀNH BÀI THI & ĐÁNH GIÁ CHUẨN ĐẦU RA",
                Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 51, 102), // ICTU Navy
                Location = new Point(20, 15),
                AutoSize = true
            };

            var lblScore = new Label
            {
                Text = $"{result.Score:F1} / 10",
                Font = new Font("Segoe UI", 36F, FontStyle.Bold),
                ForeColor = result.Score >= 8.0 ? Color.FromArgb(16, 185, 129) : (result.Score >= 6.5 ? Color.FromArgb(0, 74, 153) : Color.FromArgb(245, 158, 11)),
                Location = new Point(20, 45),
                AutoSize = true
            };

            var lblViol = new Label
            {
                Text = violations > 0 ? $"Ghi nhận {violations} cảnh báo quy chế (Alt+Tab/Copy/Paste)" : "Phòng thi ghi nhận: Thực hiện nghiêm túc quy chế thi (0 vi phạm)",
                ForeColor = violations > 0 ? Color.FromArgb(225, 29, 72) : Color.FromArgb(16, 185, 129),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(25, 125),
                AutoSize = true
            };

            pnlTop.Controls.Add(lblTitle);
            pnlTop.Controls.Add(lblScore);
            pnlTop.Controls.Add(lblViol);

            var pnlBody = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(25)
            };

            var lblAiTitle = new Label
            {
                Text = "NHẬN XÉT & ĐÁNH GIÁ NĂNG LỰC HỌC PHẦN:",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 74, 153), // ICTU Navy
                Location = new Point(25, 180),
                AutoSize = true
            };

            var rtbFeedback = new RichTextBox
            {
                Location = new Point(25, 210),
                Size = new Size(515, 230),
                ReadOnly = true,
                BackColor = Color.FromArgb(230, 240, 250), // ICTU Light
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9.5F),
                Text = result.AiFeedback + "\n\n" +
                       "📌 Lộ trình củng cố kiến thức học phần:\n" +
                       "• Tiếp tục rèn luyện kỹ năng giải thích bản chất kiến trúc và chuẩn hóa dữ liệu.\n" +
                       "• Đọc thêm giáo trình học phần và tài liệu tham khảo trên Cổng học liệu số ICTU."
            };

            var btnClose = new Button
            {
                Text = "ĐÓNG CỬA SỔ PHÒNG THI",
                Location = new Point(25, 465),
                Size = new Size(515, 48),
                BackColor = Color.FromArgb(0, 51, 102), // ICTU Navy
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => this.Close();

            this.Controls.Add(btnClose);
            this.Controls.Add(rtbFeedback);
            this.Controls.Add(lblAiTitle);
            this.Controls.Add(pnlTop);
        }
    }
}

