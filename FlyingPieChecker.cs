using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Parser.Html;
using CurlThin;
using CurlThin.Enums;
using log4net;
using System.Runtime.InteropServices;
using System.Text;
using CurlThin.Native;

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
            string iydUrl = ConfigurationManager.AppSettings["ItsYourDayUrl"];

            CurlResources.Init();
            var global = CurlNative.Init();
            var easy = CurlNative.Easy.Init();
            try
            {
                CurlNative.Easy.SetOpt(easy, CURLoption.URL, iydUrl);
                CurlNative.Easy.SetOpt(easy, CURLoption.CAINFO, "curl-ca-bundle.crt");

                var stream = new MemoryStream();
                CurlNative.Easy.SetOpt(easy, CURLoption.WRITEFUNCTION, (data, size, nmemb, user) =>
                {
                    var length = (int)size * (int)nmemb;
                    var buffer = new byte[length];
                    Marshal.Copy(data, buffer, 0, length);
                    stream.Write(buffer, 0, length);
                    return (UIntPtr)length;
                });

                var result = CurlNative.Easy.Perform(easy);
                if (result != CURLcode.OK)
                {
                    throw new Exception($"CURL failed to fetch HTML! Result: {result}");
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
            finally
            {
                easy.Dispose();

                if (global == CURLcode.OK)
                {
                    CurlNative.Cleanup();
                }
            }
        }

        /// <summary>
        /// Parses the given IYD website HTML and extracts/parses all IYD events, returning them as a list.
        /// </summary>
        private static List<IydEvent> ParseEventsFromHtml(string html)
        {
            var parser = new HtmlParser();
            var document = parser.Parse(html);

            //Get the main IYD element and then deal with their messy html to extract the actual events
            var node = document.QuerySelector(".hmy-left .txtcontent");

            List<IydEvent> events = new List<IydEvent>();
            EventParserHelper(node, events);

            SanityCheckEvents(events);

            return events;
        }

        /// <summary>
        /// Recurses over all nodes under the given node to find events and adds them to the given events list.
        /// </summary>
        private static void EventParserHelper(INode node, List<IydEvent> events)
        {
            //Take a copy of the child nodes so when we remove them we don't mess up the enumerator
            foreach (var child in node.ChildNodes)
            {
                //Recurse first
                EventParserHelper(child, events);

                //Then try to extract an event from this child
                ParseElementToEvent(child, events);
            }
        }

        /// <summary>
        /// Parses the given DOM node to an IydEvent and adds it to the given list.
        /// Ignores nodes which are not of type Text, or contain just whitespace.
        /// </summary>
        private static void ParseElementToEvent(INode node, List<IydEvent> events)
        {
            if (node.NodeType != NodeType.Text) return; //Ignore everything except actual text.
            if (string.IsNullOrWhiteSpace(node.TextContent)) return; //Ignore empty/whitespace nodes.

            var regex = new Regex(@"(?<Month>[\d]+)[\D]+(?<Day>[\d]+)[\D]+(?<Year>[\d]+)[\s:]*(?<Name>.+)", RegexOptions.IgnoreCase);
            var matches = regex.Matches(node.TextContent);
            if (matches.Count != 1) throw new ArgumentException($"Failed parsing '{node.TextContent}'");
            var match = matches[0];

            //Account for human error in the dates (typos etc.) by picking out the M/D/Y numbers separately using a more lenient regex,
            //then stuff them into a properly-formatted string that DateTime.Parse will accept.
            var dateString = $"{match.Groups["Month"]}/{match.Groups["Day"]}/{match.Groups["Year"]}";
            var date = DateTime.Parse(dateString);

            var iydEvent = new IydEvent(date, match.Groups["Name"].Value);
            events.Add(iydEvent);
        }

        /// <summary>
        /// Checks the list of events for problems, fixing them if possible.
        /// </summary>
        private static void SanityCheckEvents(List<IydEvent> events)
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
                var date = iydEvent.Date;

                //If the dates aren't all consecutive, they may have made a mess of the dates. They sometimes seem to input the wrong year, so first try to
                // adjust the year on the current date to see if that makes it consecutive. If that fails, give up and have a human look at what's going on.
                if (!AreDatesConsecutive(lastDate, date))
                {
                    //Try using the same year as the last date.
                    date = new DateTime(lastDate.Value.Year, date.Month, date.Day);
                    if (!AreDatesConsecutive(lastDate, date))
                    {
                        //If that doesn't work, try one past it (in case we're at a year transition).
                        date = new DateTime(lastDate.Value.Year + 1, date.Month, date.Day);
                        if (!AreDatesConsecutive(lastDate, date))
                        {
                            throw new InvalidDataException(string.Format("Dates from Flying Pie are not consecutive! What's wrong? lastDate: {0}, date: {1}", lastDate, iydEvent.Date));
                        }
                    }

                    //If we get here, we seem to have fixed the problem. Save it!
                    iydEvent.Date = date;
                }

                lastDate = date;
            }
        }

        private static bool AreDatesConsecutive(DateTime? lastDate, DateTime date)
        {
            //Return false if there is a previous date to compare with and they aren't consecutive.
            //Otherwise (if they are consecutive, or if there is no previous date to compare with, return true.
            return !(lastDate.HasValue && !lastDate.Value.AddDays(1).Equals(date));
        }
    }
}
