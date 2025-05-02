using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Supabase;
using Supabase.Gotrue;
using DotNetEnv;

namespace VideoChat_Client
{
    public static class SupabaseClient
    {
        private static readonly string SupabaseUrl;
        private static readonly string SupabaseKey;

        public static Supabase.Client Client { get; private set; }

        static SupabaseClient()
        {
            var options = new SupabaseOptions
            {
                AutoConnectRealtime = false
            };

            Env.TraversePath().Load();
            SupabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")!;
            SupabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY")!;

            Client = new Supabase.Client(SupabaseUrl, SupabaseKey, options);
            Initialize();
        }

        private static async void Initialize()
        {
            await Client.InitializeAsync();
        }
    }
}
