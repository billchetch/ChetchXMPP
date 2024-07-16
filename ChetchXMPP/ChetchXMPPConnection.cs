using Chetch.Utilities;
using Chetch.Messaging;
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
using XmppDotNet.Extensions.Client.PubSub;
using XmppDotNet.Extensions.Client.Roster;
using XmppDotNet.Xmpp.Roster;
using XmppDotNet.Xml;

namespace Chetch.ChetchXMPP
{
    public class ChetchXMPPConnection
    {
        public class MessageReceivedArgs : EventArgs
        {
            public XmppDotNet.Xmpp.Client.Message OriginalMessage { get; internal set; }
            public Chetch.Messaging.Message Message { get; internal set; }
        }

        
        const String CHETCH_MESSAGE_SUBJECT = "chetch.message";

        private XmppClient xmppClient;
        public SessionState CurrentState { get; internal set; } = SessionState.Disconnected;
        public String Username => xmppClient?.Jid.Bare.ToString();

        public event EventHandler<SessionState> SessionStateChanged;
        public event EventHandler<MessageReceivedArgs> MessageReceived;

        private List<String> contacts = new List<String>();

        protected void OnSessionStateChange(SessionState sessionState)
        {
            CurrentState = sessionState;
            SessionStateChanged?.Invoke(this, sessionState);
        }

        protected void OnMessageReceived(XmppDotNet.Xmpp.Client.Message message)
        {
            MessageReceivedArgs eargs = new MessageReceivedArgs();
            eargs.OriginalMessage = message;

            if (!String.IsNullOrEmpty(message.Subject) && message.Subject.Equals(CHETCH_MESSAGE_SUBJECT))
            {
                String body = message.Body;
                try
                {
                    Messaging.Message chetchMessage = Messaging.Message.Deserialize(body);
                    eargs.Message = chetchMessage;
                } catch(Exception)
                {
                    //what to do here
                }
            }


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
                throw new ChetchXMPPException(String.Format("Username {0} does not specify a domain", username));
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
                    if (v == SessionState.Binded)
                    {
                        await xmppClient.SendPresenceAsync(Show.None, "free for chat");


                        var rosterIqResult = await xmppClient.RequestRosterAsync();

                        // get all rosterItems (list of contacts)
                        var rosterItems
                            = rosterIqResult
                                .Query
                                .Cast<Roster>()
                                .GetRoster();

                        // enumerate over the items and build your contact list or GUI
                        foreach (var ri in rosterItems)
                        {
                            contacts.Add(ri.Jid.ToString());
                            
                        }
                    }
                    OnSessionStateChange(v);
                });

            //message received
            xmppClient
                .XmppXElementReceived
                .Where(el => el is XmppDotNet.Xmpp.Client.Message)
                .Subscribe(el =>
                {
                    OnMessageReceived((XmppDotNet.Xmpp.Client.Message)el);
                });
        }

        public Task ConnectAsync()
        {
            return xmppClient.ConnectAsync();
        }

        public Task DisconnectAsync()
        {
            return xmppClient.DisconnectAsync();
        }

        public Task<Iq> AddContact(String contact)
        {
            Jid jid = new Jid(contact);
            String bare = jid.Bare;
            if (!contacts.Contains(bare))
            {
                contacts.Add(bare);
            }
            return xmppClient.AddRosterItemAsync(bare);
        }

        public Task SendMessageAsync(XmppDotNet.Xmpp.Client.Message message)
        {
            return xmppClient.SendMessageAsync(message);
        }

        public Task SendMessageAsync(Messaging.Message chetchMessage)
        {
            if (String.IsNullOrEmpty(chetchMessage.Target))
            {
                throw new ChetchXMPPException("ChetchXMPPConnection::SendMessageAsync message target cannot be null or empty");
            }

            if (!chetchMessage.Target.Contains('@'))
            {
                chetchMessage.Target += '@' + xmppClient.Jid.Domain;
            }
            Jid target = new Jid(chetchMessage.Target);
            String body = chetchMessage.Serialize();


            XmppDotNet.Xmpp.Client.Message xmppMessage = new XmppDotNet.Xmpp.Client.Message();
            xmppMessage.To = target;
            xmppMessage.Subject = CHETCH_MESSAGE_SUBJECT;
            xmppMessage.Body = body;

            return SendMessageAsync(xmppMessage);
        }
    
        public void Broadcast(Messaging.Message chetchMessage)
        {
            Exception e2throw = null;
            foreach(var contact in contacts)
            {
                Messaging.Message message = new Messaging.Message(chetchMessage);
                message.Target = contact;
                try
                {
                    SendMessageAsync(message);
                } catch (Exception e)
                {
                    if(e2throw == null) {
                        e2throw = e;
                    }
                    
                }
            }
            if(e2throw != null)
            {
                throw e2throw;
            }
        }
    } //end class
}
