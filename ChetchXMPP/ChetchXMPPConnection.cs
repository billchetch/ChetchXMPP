using Chetch.Utilities;
using XmppDotNet;
using XmppDotNet.Transport.Socket;
using XmppDotNet.Xmpp.Client;
using XmppDotNet.Xmpp;
using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Threading.Tasks;
using XmppDotNet.Extensions.Client.Message;
using XmppDotNet.Extensions.Client.Presence;
using XmppDotNet.Extensions.Client.Subscription;
using System.Text;
using XmppDotNet.Xmpp.ResultSetManagement;

namespace Chetch.ChetchXMPP
{
    public class ChetchXMPPConnection
    {
        public class MessageReceivedArgs : EventArgs
        {
            public Message OriginalMessage { get; internal set; }
        }

        const String DEFAULT_XMPP_DOMAIN = "openfire@bb.lan";

        private XmppClient xmppClient;

        public event EventHandler<SessionState> SessionStateChanged;
        public event EventHandler<MessageReceivedArgs> MessageReceived;

        protected void OnSessionStateChange(SessionState sessionState)
        {
            SessionStateChanged?.Invoke(this, sessionState);
        }

        protected void OnMessageReceived(Message message)
        {
            MessageReceivedArgs eargs = new MessageReceivedArgs();
            eargs.OriginalMessage = message;
            MessageReceived?.Invoke(this, eargs);
        }

        public ChetchXMPPConnection(String username, String password)
        {
            Action<Configuration> conf = (c) =>
            {
                c.UseAutoReconnect();
                c.UseSocketTransport();
            };

            if (!username.Contains('@'))
            {
                username += "@" + DEFAULT_XMPP_DOMAIN;
            }
            xmppClient = new XmppClient(conf)
            {
                Jid = username,
                Password = password
            };

            //disable Tls
            xmppClient.Tls = false;

            //monitor state changes
            xmppClient
                .StateChanged
                .Subscribe(async v =>
                {
                    OnSessionStateChange(v);
                    if (v == SessionState.Binded)
                    {
                        await xmppClient.SendPresenceAsync(Show.None, "free for chat");
                    }
                });

            //message received
            xmppClient
                .XmppXElementReceived
                .Where(el => el is Message)
                .Subscribe(async el =>
                {
                    OnMessageReceived((Message)el);
                });
        }

        public Task ConnectAsync()
        {
            return xmppClient.ConnectAsync();
         }
    }
}
