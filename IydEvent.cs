using System;

namespace FlyingPie
{
    public class IydEvent
    {
        public DayOfWeek Weekday;
        public string Name;

        public IydEvent(DayOfWeek weekday, string name)
        {
            Weekday = weekday;
            Name = name;
        }

        public override string ToString()
        {
            return $"{Weekday}: {Name}";
        }
    }
}
