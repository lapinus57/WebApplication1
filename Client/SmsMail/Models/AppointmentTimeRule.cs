using System;
namespace Client.SmsMail.Models
{
    public sealed class AppointmentTimeRule
    {
        public TimeSpan From { get; set; } = new(11, 30, 0);
        public TimeSpan To { get; set; } = new(11, 20, 0);
    }
}
