using System;
using Client.Models;

namespace Client.Services
{
    public interface ISchoolHolidayService
    {
        bool IsSchoolHoliday(DateTime date, SchoolHolidayZone zone);
    }
}
