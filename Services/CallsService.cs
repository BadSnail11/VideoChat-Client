using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoChat_Client.Models;
using Supabase;

namespace VideoChat_Client.Services
{
    public class CallsService
    {
        private readonly Client _supabase;

        public CallsService(Client supabase)
        {
            _supabase = supabase;
        }

        public async Task<Call> StartCall(Guid callerId, Guid receiverId, string callerIp, int callerPort)
        {
            try
            {
                var call = new Call
                {
                    CallerId = callerId,
                    ReceiverId = receiverId,
                    StartedAt = DateTime.UtcNow,
                    Status = "initiated",
                    CallerIp = callerIp,
                    CallerPort = callerPort
                };

                var response = await _supabase
                    .From<Call>()
                    .Insert(call);

                return response.Model;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting call: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> UpdateCallStatus(Guid callId, string status, string receiverIp = null, int? receiverPort = null)
        {
            try
            {
                var call = await _supabase
                    .From<Call>()
                    .Where(x => x.Id == callId)
                    .Single();

                if (call == null) return false;

                call.Status = status;

                if (status == "completed" || status == "missed" || status == "rejected")
                {
                    call.EndedAt = DateTime.UtcNow;
                }

                if (receiverIp != null)
                    call.ReceiverIp = receiverIp;

                if (receiverPort.HasValue)
                    call.ReceiverPort = receiverPort.Value;

                await _supabase
                    .From<Call>()
                    .Where(x => x.Id == callId)
                    .Set(x => x.Status, call.Status)
                    .Set(x => x.EndedAt, call.EndedAt)
                    .Set(x => x.ReceiverIp, call.ReceiverIp)
                    .Set(x => x.ReceiverPort, call.ReceiverPort)
                    .Update();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating call status: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UpdateCallDuration(Guid callId, TimeSpan duration)
        {
            try
            {
                string formattedDuration = FormatDuration(duration);

                await _supabase
                    .From<Call>()
                    .Where(x => x.Id == callId)
                    .Set(x => x.Duration, formattedDuration)
                    .Update();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating call duration: {ex.Message}");
                return false;
            }
        }

        public async Task<List<Call>> GetCallHistory(Guid userId, Guid contactId)
        {
            try
            {
                var response = await _supabase
                    .From<Call>()
                    .Where(x => (x.CallerId == userId && x.ReceiverId == contactId) ||
                               (x.CallerId == contactId && x.ReceiverId == userId))
                    .Order(x => x.StartedAt, Postgrest.Constants.Ordering.Descending)
                    .Get();

                return response.Models.Select(c =>
                {
                    var call = new Call
                    {
                        Id = c.Id,
                        CallerId = c.CallerId,
                        ReceiverId = c.ReceiverId,
                        StartedAt = c.StartedAt,
                        EndedAt = c.EndedAt,
                        Status = c.Status,
                        CallerIp = c.CallerIp,
                        ReceiverIp = c.ReceiverIp,
                        CallerPort = c.CallerPort,
                        ReceiverPort = c.ReceiverPort
                    };

                    if (call.EndedAt.HasValue && call.StartedAt != null)
                    {
                        call.Duration = FormatDuration(call.EndedAt.Value - call.StartedAt);
                    }
                    else
                    {
                        call.Duration = "N/A";
                    }

                    return call;
                }).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting call history: {ex.Message}");
                return new List<Call>();
            }
        }

        public async Task<List<Call>> GetAllUserCalls(Guid userId)
        {
            try
            {
                var response = await _supabase
                    .From<Call>()
                    .Where(x => x.CallerId == userId || x.ReceiverId == userId)
                    .Order(x => x.StartedAt, Postgrest.Constants.Ordering.Descending)
                    .Get();

                return response.Models.Select(c => new Call
                {
                    Id = c.Id,
                    CallerId = c.CallerId,
                    ReceiverId = c.ReceiverId,
                    StartedAt = c.StartedAt,
                    EndedAt = c.EndedAt,
                    Status = c.Status,
                    CallerIp = c.CallerIp,
                    ReceiverIp = c.ReceiverIp,
                    CallerPort = c.CallerPort,
                    ReceiverPort = c.ReceiverPort,
                    Duration = c.EndedAt.HasValue
                        ? FormatDuration(c.EndedAt.Value - c.StartedAt)
                        : "N/A"
                }).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting all user calls: {ex.Message}");
                return new List<Call>();
            }
        }

        private string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return duration.ToString(@"hh\:mm\:ss");

            return duration.ToString(@"mm\:ss");
        }
    }
}
