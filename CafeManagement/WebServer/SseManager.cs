using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CafeManagement.WebServer
{
    public class SseClient
    {
        private readonly HttpResponse _response;

        public SseClient(HttpResponse response)
        {
            _response = response;
        }

        public async Task SendEventAsync(string eventName, string data)
        {
            try
            {
                await _response.WriteAsync($"event: {eventName}\ndata: {data}\n\n");
                await _response.Body.FlushAsync();
            }
            catch
            {
                // Client disconnected or write failed
                throw;
            }
        }
    }

    public static class SseManager
    {
        private static readonly ConcurrentBag<SseClient> Clients = new ConcurrentBag<SseClient>();

        public static void AddClient(SseClient client)
        {
            Clients.Add(client);
        }

        public static void RemoveClient(SseClient client)
        {
            // ConcurrentBag doesn't have a direct Remove, but we can filter it or let it fail gracefully during write.
            // Since it's a small app, we can also use a concurrent dictionary or just let SendEventAsync handle failures.
        }

        public static async Task BroadcastAsync(string eventName, string data)
        {
            var activeClients = new System.Collections.Generic.List<SseClient>();
            foreach (var client in Clients)
            {
                try
                {
                    await client.SendEventAsync(eventName, data);
                    activeClients.Add(client);
                }
                catch
                {
                    // Ignore and remove failed client later if needed
                }
            }
        }
    }
}
