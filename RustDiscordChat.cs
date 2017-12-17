using System;
using System.Collections.Generic;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("RustDiscordChat", "Kyzeragon", "1.0.0")]
    [Description("Send ingame Rust messages to Discord and vice versa")]
    class RustDiscordChat : RustPlugin
    {
        ///////////////////////////////////////////////////////////////////////

        // Referenced RustNotifications and Discord by seanbyrne88

        ///////////////////////////////////////////////////////////////////////

        private static PluginConfig settings;
        private static string lastMessageID = "";
        private static string sendMessageURL = "https://discordapp.com/api/channels/{{channelID}}/messages";
        private static string getMessagesURL = "https://discordapp.com/api/channels/{{channelID}}/messages";
        private static Timer task;
        private static Dictionary<string, string> emojiMappings = new Dictionary<string, string>
        {
            {"😄", ":D"},
            {"😮", ":O"},
            {"😃", ":)"},
            {"😛", ":P"},
            {"😉", ";)"},
        };

        ///////////////////////////////////////////////////////////////////////
        // Oxide

        void Init()
        {
            LoadConfigValues();

            // Start recurring task for fetching messages
            if (settings.sendDiscordToGame)
            {
                double interval = settings.checkDiscordInterval;
                if (interval < 1.0)
                    interval = 1.0;
                task = timer.Repeat((float)interval, 0, checkDiscord);
            }
        }

        /**
         *  Remember to destroy timers on unload!
         *  Learned that the hard way...
         */
        void Unload()
        {
            if (task != null)
                task.Destroy();
        }

        /**
         *  On player chat, send the message to Discord
         */
        void OnPlayerChat(ConsoleSystem.Arg arg)
        {
            if (!settings.sendGameToDiscord)
                return;

            BasePlayer player = (BasePlayer)arg.Connection.player;

            string message = "";
            foreach (string argString in arg.Args)
                message += argString + " ";

            string text = lang.GetMessage("gameToDiscordChat", this, "").Replace("{displayName}", player.displayName).Replace("{message}", message);
            sendDiscordMessage(text);
        }

        /**
         *  Messages sent from server, send to Discord.
         */
        void OnServerMessage(string message, string name, string color, ulong id)
        {
            if (!settings.sendGameToDiscord)
                return;

            string text = lang.GetMessage("gameToDiscordChat", this, "").Replace("{displayName}", name).Replace("{message}", message);
            sendDiscordMessage(text);
        }

        /**
         *  On player join, send message to Discord
         */
        void OnPlayerInit(BasePlayer player)
        {
            if (!settings.announceJoin)
                return;

            string text = lang.GetMessage("playerJoinGame", this, "").Replace("{displayName}", player.displayName);
            sendDiscordMessage(text);
        }

        /**
         *  On player disconnect, send message to Discord
         */
        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (!settings.announceLeave)
                return;

            string text = lang.GetMessage("playerLeaveGame", this, "").Replace("{displayName}", player.displayName).Replace("{reason}", reason);
            sendDiscordMessage(text);
        }


        ///////////////////////////////////////////////////////////////////////
        // Discord

        /**
         *  Callback for POST. Not much to do except log a warning
         *  that the message couldn't be sent.
         */
        private void postCallBack(int code, string response)
        {
            if (code != 200)
                PrintWarning(String.Format("POST: Discord API responded with {0}: {1}", code, response));
        }

        /**
         *  Send a message to specified Discord channel using a POST request.
         */
        void sendDiscordMessage(string message)
        {
            string payloadJson = "{ \"content\": \"" + message + "\" }";

            Dictionary<string, string> headers = new Dictionary<string, string>();
            if (settings.botToken.StartsWith("Bot "))
                headers.Add("Authorization", settings.botToken);
            else
                headers.Add("Authorization", String.Format("Bot {0}", settings.botToken));
            headers.Add("Content-Type", "application/json");

            string url = sendMessageURL.Replace("{{channelID}}", settings.channelID);
            webrequest.EnqueuePost(url, payloadJson, (code, response) => postCallBack(code, response), this, headers);
        }

        /**
         *  Callback for GET. If printing is enabled, it will print the
         *  messages from the response ingame and to the console.
         *  The last message ID is cached to use in future GET requests.
         */
        private void getCallBack(int code, string response, bool doPrint)
        {
            if (code != 200)
            {
                PrintWarning(String.Format("GET: Discord API responded with {0}: {1}", code, response));
                return;
            }

            // Serialize
            List<DiscordMessage> json = JsonConvert.DeserializeObject<List<DiscordMessage>>(response);

            if (json.Count > 0)
            {
                // Set the latest message ID
                lastMessageID = json[0].id;

                if (doPrint)
                {
                    // Loop backwards because latest is first
                    for (int i = json.Count - 1; i >= 0; i--)
                    {
                        if (!json[i].author.bot) // Ignore all bots for now, could be config later
                        {
                            string text = lang.GetMessage("discordToGameChat", this, "")
                            .Replace("{username}", json[i].author.username)
                            .Replace("{message}", translateEmojis(json[i].content));
                            sendAllIngame(text);
                        }
                    }
                }
            }
        }

        /**
         *  Sends a GET request to get messages from specified channel.
         */
        private void getMessages(string query, bool doPrint)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();
            if (settings.botToken.StartsWith("Bot "))
                headers.Add("Authorization", settings.botToken);
            else
                headers.Add("Authorization", String.Format("Bot {0}", settings.botToken));

            string url = getMessagesURL.Replace("{{channelID}}", settings.channelID);
            webrequest.EnqueueGet(url + query, (code, response) => getCallBack(code, response, doPrint), this, headers);
        }

        /**
         *  Recurring task to call getMessages
         */
        private void checkDiscord()
        {
            // Grab the latest message ID first, if there isn't one
            if (lastMessageID == "")
            {
                Puts("Initializing last message...");
                getMessages("?limit=1", false);
                return;
            }

            // Get new messages
            getMessages("?after=" + lastMessageID, true);
        }


        ///////////////////////////////////////////////////////////////////////
        // Helpers

        /**
         *  Send a message to all ingame players, as well as log to console
         */
        private void sendAllIngame(string message)
        {
            Puts(message);
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                PrintToChat(player, message);
            }
        }

        /**
         *  Replaces some unicode emojis with text
         */
        private string translateEmojis(string message)
        {
            foreach (KeyValuePair<string, string> pair in emojiMappings)
            {
                message = message.Replace(pair.Key, pair.Value);
            }
            return message;
        }


        ///////////////////////////////////////////////////////////////////////
        // Localization

        private void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
                {
                    {"gameToDiscordChat", "**[{displayName}]** {message}"},
                    {"discordToGameChat", "[D] {username}: {message}"},
                    {"playerJoinGame", "**{displayName}** has joined the server!"},
                    {"playerLeaveGame", "**{displayName}** has left the server! Reason: {reason}"}
                }, this);
        }


        ///////////////////////////////////////////////////////////////////////
        // Config

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            Config.WriteObject(DefaultConfig(), true);

            PrintWarning("Default Configuration File Created");
            LoadDefaultMessages();
            PrintWarning("Default Language File Created");
        }

        private void LoadConfigValues()
        {
            settings = Config.ReadObject<PluginConfig>();
        }

        private PluginConfig DefaultConfig()
        {
            return new PluginConfig
            {
                botToken = String.Empty,
                channelID = String.Empty,

                announceJoin = true,
                announceLeave = true,
                sendGameToDiscord = true,
                sendDiscordToGame = true,

                // Time in seconds between checking Discord
                checkDiscordInterval = 5.0
            };
        }


        ///////////////////////////////////////////////////////////////////////
        // Classes

        private class PluginConfig
        {
            public string botToken { get; set; }
            public string channelID { get; set; }
            public double checkDiscordInterval { get; set; }

            public bool announceJoin { get; set; }
            public bool announceLeave { get; set; }
            public bool sendGameToDiscord { get; set; }
            public bool sendDiscordToGame { get; set; }
        }

        // Used to serialize response from Discord
        private class DiscordMessage
        {
            public string content { get; set; }
            public string id { get; set; }
            public AuthorInfo author { get; set; }
        }

        // Used to serialize response from Discord
        private class AuthorInfo
        {
            public string username { get; set; }
            public bool bot { get; set; }
        }

    }
}
