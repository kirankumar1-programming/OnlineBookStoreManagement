namespace OnlineBookStoreManagement.Services
{
    public class SmtpSettings
    {
        public string Host { get; set; } = "smtp.gmail.com";
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string UserName { get; set; } = "kiran.kumar1@programming.com";
        public string Password { get; set; } = "nobo ylst kmbo atin";
        public string SenderEmail { get; set; } = "kiran.kumar1@programming.com";
        public string SenderName { get; set; } = "My Book Store";
        public bool EnableEmailNotifications { get; set; } = true;
    }
}
