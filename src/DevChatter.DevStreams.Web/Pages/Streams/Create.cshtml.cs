﻿using DevChatter.DevStreams.Web.Data.ViewModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using NodaTime;
using NodaTime.Extensions;
using NodaTime.Text;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using TimeZoneNames;

namespace DevChatter.DevStreams.Web.Pages.Streams
{
    public class CreateModel : PageModel
    {
        public static readonly ZonedDateTimePattern ZonedDateTimePattern = 
            ZonedDateTimePattern.CreateWithInvariantCulture("yyyy-MM-dd HH:mm o<G>", null);


        private readonly IClock _clock;

        public CreateModel()
        {
            _clock = SystemClock.Instance;
        }

        [BindProperty]
        public CreateStreamTimeViewModel StreamTime { get; set; } = new CreateStreamTimeViewModel
        {
            DayOfWeek = IsoDayOfWeek.Monday,
            LocalStartTime = "14:00",
            LocalEndTime = "16:00",
        };

        public IEnumerable<SelectListItem> TimeZoneIds;
        public IEnumerable<SelectListItem> Countries;
        public List<ZonedDateTime> SuggestedSessions = new List<ZonedDateTime>();
        public void OnGet()
        {
            IDateTimeZoneProvider dateTimeZoneProvider = DateTimeZoneProviders.Tzdb;
            var tzdbIds = dateTimeZoneProvider.Ids;

            // TODO: Use better names than the ID itself.
            TimeZoneIds = tzdbIds.Select(x => new SelectListItem(x, x));

            Countries = TZNames.GetCountryNames(CultureInfo.CurrentUICulture.Name)
                .Select(x => new SelectListItem(x.Value, x.Key));

            //var localTime = new LocalTime(14,0,0,0);
            //string formatted = LocalTimePattern.ExtendedIso.Format(localTime);

            //var parsed = LocalTimePattern.ExtendedIso.Parse(formatted).Value;
        }

        public void OnPost()
        {
            var streamTime = StreamTime.ToModel();

            var zone = DateTimeZoneProviders.Tzdb[StreamTime.TimeZoneId];
            //var version = DateTimeZoneProviders.Tzdb.VersionId;
            ZonedClock zonedClock = _clock.InZone(zone);

            LocalDate today = zonedClock.GetCurrentDate();
            LocalDate next = today.With(DateAdjusters.Next(streamTime.DayOfWeek));

            SuggestedSessions = new List<ZonedDateTime>();
            for (int i = 0; i < 52; i++)
            {
                LocalDateTime nextLocalDateTime = next + streamTime.LocalStartTime;

                SuggestedSessions.Add(nextLocalDateTime.InZoneLeniently(zone));
                next = next.PlusWeeks(1);
            }
        }
    }
}