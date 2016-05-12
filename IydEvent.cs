using System;

namespace FlyingPie
{
    public class IydEvent
    {
        public DateTime Date;
        public string Name;

        public IydEvent(DateTime date, string name)
        {
            Date = date;
            Name = name;
        }

        public string GetNameForCalendar()
        {
            if (Name.Contains("Gourmet Night"))
            {
                return "FP Gourmet Night";
            }

            return $"IYD: {Name}";
        }

        public override string ToString()
        {
            return $"{Date:d} - {Name}";
        }
    }
}
