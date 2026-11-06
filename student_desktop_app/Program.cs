using System;
using System.Windows.Forms;

namespace student_desktop_app
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();

            bool useLegacy = false;
            string serverUrl = "http://localhost:5000/student_web.html?mode=desktop_kiosk";

            foreach (var arg in args)
            {
                if (arg.Equals("--legacy", StringComparison.OrdinalIgnoreCase))
                {
                    useLegacy = true;
                }
                else if (arg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    serverUrl = arg;
                }
            }

            // Luôn mở giao diện Đăng nhập (LoginForm) trước tiên khi khởi chạy chương trình
            Application.Run(new LoginForm(serverUrl, useLegacy));
        }
    }
}
