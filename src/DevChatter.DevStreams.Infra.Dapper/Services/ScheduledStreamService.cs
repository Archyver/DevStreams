﻿using Dapper;
using DevChatter.DevStreams.Core.Model;
using DevChatter.DevStreams.Core.Services;
using DevChatter.DevStreams.Core.Settings;
using Microsoft.Extensions.Options;
using NodaTime;
using NodaTime.Extensions;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace DevChatter.DevStreams.Infra.Dapper.Services
{
    public class ScheduledStreamService : IScheduledStreamService
    {
        private readonly DatabaseSettings _dbSettings;
        private readonly IClock _clock;

        private const string scheduledSql = "INSERT INTO ScheduledStreams (ChannelId, DayOfWeek, LocalStartTime, LocalEndTime, TimeZoneId) Values (@ChannelId, @DayOfWeek, @LocalStartTime, @LocalEndTime, @TimeZoneId); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";


        private const string sessionSql = "INSERT INTO StreamSessions (ChannelId, ScheduledStreamId, UtcStartTime, UtcEndTime, TzdbVersionId) Values (@ChannelId, @ScheduledStreamId, @UtcStartTime, @UtcEndTime, @TzdbVersionId); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";


        public ScheduledStreamService(IOptions<DatabaseSettings> dbSettings, IClock clock)
        {
            _dbSettings = dbSettings.Value;
            _clock = clock;
        }

        public async Task<List<ScheduledStream>> GetChannelSchedule(int channelId)
        {
            const string sql = "SELECT * FROM ScheduledStreams WHERE ChannelId = @ChannelId";
            using (IDbConnection connection = new SqlConnection(_dbSettings.DefaultConnection))
            {
                var where = new { ChannelId = channelId };
                return (await connection.QueryAsync<ScheduledStream>(sql, where)).ToList();

                // TODO: Use this when SimpleCRUD is fixed.
                //return (await connection.GetListAsync<ScheduledStream>(where)).ToList();
            }
        }

        public async Task<int?> AddScheduledStreamToChannel(ScheduledStream stream)
        {
            using (IDbConnection connection = new SqlConnection(_dbSettings.DefaultConnection))
            {
                int id = (await connection.QuerySingleAsync<int>(scheduledSql, stream));

                // TODO: Use this once issue fixed in SimpleCRUD
                //int? id = await connection.InsertAsync(stream);

                stream.Id = id;
                var timeZone = DateTimeZoneProviders.Tzdb[stream.TimeZoneId];
                var sessions = CreateStreamSessions(stream, timeZone);

                foreach (var session in sessions)
                {
                    connection.QuerySingle<int>(sessionSql, session);
                }

                // TODO: Use this once issue fixed in SimpleCRUD
                //await Task.WhenAll(sessions.Select(s => connection.InsertAsync(s)));

                return id;
            }
        }

        public async Task<int> Delete(int id)
        {
            using (IDbConnection connection = new SqlConnection(_dbSettings.DefaultConnection))
            {
                // Delete All Sessions
                await connection.DeleteListAsync<StreamSession>(new {ScheduledStreamId = id});

                // Delete Scheduled Stream
                int deleteCount = await connection.DeleteAsync<ScheduledStream>(id);

                return deleteCount;
            }
        }

        public async Task<int> Update(ScheduledStream stream)
        {
            using (IDbConnection connection = new SqlConnection(_dbSettings.DefaultConnection))
            {
                int updateCount = await connection.UpdateAsync(stream);

                if (updateCount > 0)
                {
                    await connection.DeleteListAsync<StreamSession>(
                        new { ScheduledStreamId = stream.Id });

                    var timeZone = DateTimeZoneProviders.Tzdb[stream.TimeZoneId];
                    var sessions = CreateStreamSessions(stream, timeZone);

                    await Task.WhenAll(sessions.Select(s => connection.InsertAsync(s)));
                }

                return updateCount;
            }
        }

        private List<StreamSession> CreateStreamSessions(
            ScheduledStream stream, DateTimeZone timeZone)
        {
            List<StreamSession> sessions = new List<StreamSession>();
            ZonedClock zonedClock = _clock.InZone(timeZone);

            LocalDate nextOfDay = zonedClock.GetCurrentDate()
                .With(DateAdjusters.Next(stream.DayOfWeek));

            for (int i = 0; i < 52; i++)
            {
                LocalDateTime nextLocalStartDateTime = nextOfDay + stream.LocalStartTime;
                LocalDateTime nextLocalEndDateTime = nextOfDay + stream.LocalEndTime;

                var streamSession = new StreamSession
                {
                    ChannelId = stream.ChannelId,
                    ScheduledStreamId = stream.Id,
                    TzdbVersionId = DateTimeZoneProviders.Tzdb.VersionId,
                    UtcStartTime = nextLocalStartDateTime
                        .InZoneLeniently(timeZone)
                        .ToInstant(),
                    UtcEndTime = nextLocalEndDateTime.InZoneLeniently(timeZone).ToInstant(),
                };

                sessions.Add(streamSession);

                nextOfDay = nextOfDay.PlusWeeks(1);
            }

            return sessions;
        }
    }
}