using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
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

        public void RunCheck()
        {
            log.Debug("Checking...");

            var events = GetEvents();
            string newData = FormatEventsForEmail(events);

            const string configKey = "LastEmailData";
            string oldData = ConfigurationManager.AppSettings[configKey];
            if (oldData != newData)
            {
                log.Debug("New or changed event data found.");
                if (Email.SendNotificationMail(newData))
                {
                    log.Debug("Notification email sent.");
                    Utilities.SetAppConfig(configKey, newData);
                }
            }

            log.Debug("Check complete.\n");
        }

        /// <summary>
        /// Formats the given list of events so it's ready for emailing.
        /// Currently this just uses each event's ToString method and separates them with newlines.
        /// </summary>
        private string FormatEventsForEmail(List<IydEvent> events)
        {
            return string.Join("\n", events);
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
            var document = parser.ParseDocument(html);

            //Get the main IYD element
            var selector = "div[data-block='true']";
            var parent = document.QuerySelectorAll(selector);
            if (parent == null)
            {
                throw new Exception($"Failed to query selector '{selector}' to find parent node. Has page HTML changed?");
            }
            if (parent.Count() > 1)
            {
                throw new Exception($"Got too many results querying selector '{selector}'! Has page HTML changed?");
            }

            //Get all of its children and select the ones containing a colon - those should be the IYD events
            var nodes = parent.First().Children.Where(child => child.TextContent.Contains(":"));
            if (nodes == null || nodes.Count() <= 0)
            {
                throw new Exception($"Failed to find any content nodes under parent. Has page HTML changed?");
            }

            //Traverse the main element's children to extract events
            List<IydEvent> events = new List<IydEvent>();
            EventParserHelper(nodes.First(), events);

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

            var regex = new Regex(@"(?<Weekday>[a-z]+day)[\s:]+(?<Name>.+)", RegexOptions.IgnoreCase);
            var matches = regex.Matches(node.TextContent);
            if (matches.Count != 1) throw new ArgumentException($"Failed parsing '{node.TextContent}'");
            var match = matches[0];

            var weekday = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), match.Groups["Weekday"].Value, true);
            var iydEvent = new IydEvent(weekday, match.Groups["Name"].Value);
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
        }
    }
}
