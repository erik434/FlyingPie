using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Parser.Html;
using log4net;

namespace FlyingPie
{
    public class FlyingPieChecker
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private const string CalendarName = "Flying Pie - IYD";
        private const string ApplicationName = "flying-pie-iyd-app";

        public void RunCheck()
        {
            log.Debug("Checking...");

            var events = GetEvents();
            var newOrChangedEvents = new List<IydEvent>();

            using (var service = CalendarApi.GetCalendarService(ApplicationName, CalendarName))
            {
                //Then create/update all events as needed, saving new/modified events so we can send an email about them.
                newOrChangedEvents.AddRange(events.Where(iydEvent => service.CreateOrUpdateEvent(iydEvent.Date, iydEvent.GetNameForCalendar())));
            }

            Email.SendNotificationMail(newOrChangedEvents);

            log.Debug("Check complete.\n");
        }

        /// <summary>
        /// Fetches the latest IYD events from the IYD website and returns them in a list.
        /// </summary>
        private static List<IydEvent> GetEvents()
        {
            string html = GetSiteHtml();
            var events = ParseEventsFromHtml(html);
            log.DebugFormat("Parsed {0} events from website", events.Count);
            return events;
        }

        /// <summary>
        /// Downloads the IYD website and returns its HTML as a string.
        /// </summary>
        private static string GetSiteHtml()
        {
            using (var client = new WebClient())
            {
                string iydUrl = ConfigurationManager.AppSettings["ItsYourDayUrl"];
                return client.DownloadString(iydUrl);
            }
        }

        /// <summary>
        /// Parses the given IYD website HTML and extracts/parses all IYD events, returning them as a list.
        /// </summary>
        private static List<IydEvent> ParseEventsFromHtml(string html)
        {
            var parser = new HtmlParser();
            var document = parser.Parse(html);
            var events = document.QuerySelectorAll("#dayname p").Where(e => !string.IsNullOrWhiteSpace(e.TextContent)).Select(ParseElementToEvent).ToList();

            CheckEvents(events);

            return events;
        }

        /// <summary>
        /// Checks the list of events for problems, fixing them if possible.
        /// </summary>
        private static void CheckEvents(IList<IydEvent> events)
        {
            //If no events were found, they probably changed their website and this code needs to be updated!
            if (!events.Any())
            {
                throw new InvalidDataException("No events were found! Did Flying Pie change their website?");
            }

            var now = DateTime.Now;
            DateTime? lastDate = null;
            foreach (var iydEvent in events)
            {
                //If the date is too far away, there may be a typo in the year - fix it to the current year.
                //They did it once already (mm/dd/15 instead of mm/dd/16) - they may do it again.
                var date = iydEvent.Date;
                if ((now - date).TotalDays > 30)
                {
                    date = new DateTime(now.Year, date.Month, date.Day);
                    iydEvent.Date = date;
                }

                //If the dates aren't all consecutive, they may have made a mess of the dates - should have a human look at this
                if (lastDate.HasValue && !lastDate.Value.AddDays(1).Equals(date))
                {
                    throw new InvalidDataException("Dates from Flying Pie are not consecutive! What's wrong?");
                }
                lastDate = date;
            }
        }

        /// <summary>
        /// Parses the given DOM element to an IydEvent - may need updated if Flying Pie changes their website layout.
        /// </summary>
        private static IydEvent ParseElementToEvent(IElement element)
        {
            var regex = new Regex(@"(?<Date>[\d/]+):[\s]+(?<Name>.+)", RegexOptions.IgnoreCase);
            var matches = regex.Matches(element.TextContent);
            if (matches.Count != 1) throw new ArgumentException($"Failed parsing '{element.TextContent}'");

            var match = matches[0];
            var date = DateTime.Parse(match.Groups["Date"].Value);

            return new IydEvent(date, match.Groups["Name"].Value);
        }
    }
}
