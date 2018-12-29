using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Util.Store;
using log4net;
using Microsoft.Practices.TransientFaultHandling;

namespace FlyingPie
{
    public class CalendarApi : CalendarService
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private const string AllDayDateFormat = "yyyy-MM-dd";
        private readonly RetryPolicy _retryPolicy = RetryPolicy.DefaultExponential;
        
        public CalendarListEntry CurrentCalendar { get; private set; }

        private CalendarApi(Initializer initializer)
            : base(initializer)
        {
            _retryPolicy.Retrying += RetryInvoked;
        }

        private static void RetryInvoked(object sender, RetryingEventArgs e)
        {
            log.DebugFormat("Retry invoked. Current retry count: {0}. Exception: {1}", e.CurrentRetryCount, e.LastException);
        }

        public static CalendarApi GetCalendarService(string applicationName, string calendarName)
        {
            // If modifying these scopes, delete your previously saved credentials at ~/.credentials/applicationName.json
            string[] scopes = { Scope.Calendar };

            //Load cached credentials, or obtain fresh authorization from the user.
            UserCredential credential;
            using (var stream = new FileStream("client_secret.json", FileMode.Open, FileAccess.Read))
            {
                string credPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                credPath = Path.Combine(credPath, ".credentials", applicationName + ".json");

                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).Result;
                log.DebugFormat("Using credential file: {0}", credPath);
            }

            //Create a new CalendarApi using the credentials etc.
            var api = new CalendarApi(new Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = applicationName,
            });

            //Set the CurrentCalendar to the calendar with the given name (or create it if it doesn't already exist)
            api.CurrentCalendar = api.GetOrCreateCalendar(calendarName);

            return api;
        }

        /// <summary>
        /// Returns the all-day event on the given date (on CurrentCalendar).
        /// </summary>
        public Event GetEvent(DateTime date)
        {
            //Search for an all-day event for the given date - TimeMin must be the given day, and TimeMax the next.
            var eventsRequest = Events.List(CurrentCalendar.Id);
            eventsRequest.SingleEvents = true;
            eventsRequest.ShowDeleted = false;
            eventsRequest.TimeMin = Utilities.FlyingPieLocalDateToUtcDateTime(date.Year, date.Month, date.Day);
            var nextDay = date.AddDays(1);
            eventsRequest.TimeMax = Utilities.FlyingPieLocalDateToUtcDateTime(nextDay.Year, nextDay.Month, nextDay.Day);

            return _retryPolicy.ExecuteAction(() => eventsRequest.Execute().Items.FirstOrDefault());
        }

        /// <summary>
        /// Creates an all-day event on the given date (on CurrentCalendar) with the given name.
        /// </summary>
        public Event CreateEvent(DateTime date, string eventName)
        {
            //Ensure that the given DateTime is really just a date
            date = date.Date;

            //Create & insert the new event
            var dayEvent = new Event
            {
                Summary = eventName,
                Start = new EventDateTime { Date = date.ToString(AllDayDateFormat) },
                End = new EventDateTime { Date = date.AddDays(1).ToString(AllDayDateFormat) },
                Description = $"'{eventName}' created: {DateTime.Now:F}\n"
            };

            log.InfoFormat("Adding event on {0:d}: '{1}'", date, eventName);
            var result = _retryPolicy.ExecuteAction(() => Events.Insert(dayEvent, CurrentCalendar.Id).Execute());
            if (result == null)
            {
                throw new Exception("Insert returned a null event.");
            }

            return result;
        }

        /// <summary>
        /// Updates the all-day event on the given date (on CurrentCalendar) with the given name, if necessary.
        /// Returns true if this resulted in changes, or false if the given data was already present in the calendar.
        /// </summary>
        public bool UpdateEvent(Event dayEvent, string eventName)
        {
            //If the summary is already correct, just return the event, we're done.
            if (dayEvent.Summary == eventName) return false;

            //Otherwise set the summary and update it.
            string oldSummary = dayEvent.Summary;
            dayEvent.Summary = eventName;
            dayEvent.Description = (dayEvent.Description ?? string.Empty) + $"'{eventName}' updated: {DateTime.Now:F}\n";

            log.InfoFormat("Updating event '{0}' on {1} with new name '{2}'", oldSummary, dayEvent.Start.Date, eventName);
            var result = _retryPolicy.ExecuteAction(() => Events.Update(dayEvent, CurrentCalendar.Id, dayEvent.Id).Execute());
            if (result == null)
            {
                throw new Exception("Update returned a null event.");
            }
            return true;
        }

        /// <summary>
        /// Creates or updates the all-day event on the given date (on CurrentCalendar) with the given name.
        /// Returns true if this resulted in any actual changes, false if the given data is already on the calendar.
        /// </summary>
        public bool CreateOrUpdateEvent(DateTime date, string eventName)
        {
            //Ensure that the given DateTime is really just a date
            date = date.Date;

            //Check for an event on the given day. If there isn't anything, create one. Otherwise, update the existing event with the new name.
            var dayEvent = GetEvent(date);
            if (dayEvent == null)
            {
                CreateEvent(date, eventName);
                return true;
            }

            return UpdateEvent(dayEvent, eventName);
        }

        /// <summary>
        /// Gets or creates a calendar with the given name and sets CurrentCalendar to match.
        /// </summary>
        private CalendarListEntry GetOrCreateCalendar(string calendarName)
        {
            var calendar = GetCalendar(calendarName) ?? CreateCalendar(calendarName);
            return calendar;
        }

        /// <summary>
        /// Creates a calendar with the given name and sets CurrentCalendar to match.
        /// </summary>
        private CalendarListEntry CreateCalendar(string calendarName)
        {
            Calendar calendar = new Calendar
            {
                Summary = calendarName,
                Location = "Boise, ID",
                TimeZone = "America/Denver", //This is MST
            };

            log.InfoFormat("Creating calendar '{0}'", calendarName);
            var result = _retryPolicy.ExecuteAction(() => Calendars.Insert(calendar).Execute());
            if (result == null)
            {
                throw new Exception("Insert returned a null calendar.");
            }

            return GetCalendar(calendarName);
        }

        /// <summary>
        /// Gets the calendar with the given name.
        /// </summary>
        private CalendarListEntry GetCalendar(string calendarName)
        {
            var getCalendarsRequest = CalendarList.List();
            getCalendarsRequest.ShowDeleted = false;
            getCalendarsRequest.ShowHidden = true;
            var calendars = _retryPolicy.ExecuteAction(() => getCalendarsRequest.Execute());

            var calendar = calendars.Items.FirstOrDefault(c => calendarName.Equals(c.Summary) || calendarName.Equals(c.SummaryOverride));
            log.DebugFormat(calendar == null ? "Failed to find calendar '{0}'" : "Found calendar '{0}'", calendarName);
            return calendar;
        }
    }
}
