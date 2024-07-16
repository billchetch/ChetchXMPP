using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Services;
using Microsoft.Extensions.Logging;
using XmppDotNet.Transport;
using Chetch.Messaging;
using Microsoft.Extensions.Configuration;
using XmppDotNet;
using XmppDotNet.Xmpp.Sasl;
using System.ComponentModel;

namespace Chetch.ChetchXMPP
{
    public class ChetchXMPPService(ILogger<ChetchXMPPService> logger) : Service<ChetchXMPPService>(logger)
    {
        #region Class declarations
        enum ServiceEvent
        {
            None = 0, //for initialising
            Connected = 1,
            Disconnecting = 2,
            Stopping = 3,
        }
        #endregion

        #region Fields
        ChetchXMPPConnection cnn;
        Dictionary<String, String> commandHelp = new Dictionary<String, String>();
        #endregion

        #region Service lifecycle
        override protected async Task Execute(CancellationToken stoppingToken)
        {
            //unnecessary wait???
            await Task.Delay(100);

            //do some config
            var config = getAppSettings();
            String username = config.GetValue<String>("Credentials:Username");
            String password = config.GetValue<String>("Credentials:Password");
            logger.LogInformation(88, "Creating XMPP connection for user {0}...", username);

            //create the connection
            cnn = new ChetchXMPPConnection(username, password);
            
            //Set event handlers
            cnn.SessionStateChanged += (sender, state) => {
                try
                {
                    SessionStateChanged(state);
                } catch (Exception e)
                {
                    logger.LogError(1188, e, e.Message);
                }
            };

            cnn.MessageReceived += (sender, eargs) =>
            {
                if (eargs.Message != null)
                {
                    Message response = CreateResponse(eargs.Message);
                    if (messageReceived(eargs.Message, response))
                    {
                        try
                        {
                            SendMessage(response);
                        }
                        catch (Exception e)
                        {
                            logger.LogError(1288, e, e.Message);
                        }
                    }
                }
            };

            //Now connect
            Task connectTask = cnn.ConnectAsync();
            try
            {
                logger.LogInformation(188, "Awaiting connect process to complete...");
                await connectTask;
                logger.LogInformation(189, "Connect process completed!");

            }
            catch (Exception e)
            {
                logger.LogError(1188, e, e.Message);
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            String eventDescription = String.Format("{0} is stopping", cnn.Username);
            Message notification = createNotificationOfEvent(ServiceEvent.Stopping, eventDescription);
            Broadcast(notification);
            cnn.DisconnectAsync();

            return base.StopAsync(cancellationToken);
        }
        #endregion

        #region XMPP connection handlers
        protected void SessionStateChanged(SessionState newState)
        {
            ServiceEvent serviceEvent = ServiceEvent.None;
            String eventDescription = String.Empty;
            switch (newState)
            {
                case SessionState.Binded:
                    serviceEvent = ServiceEvent.Connected;
                    eventDescription = String.Format("{0} is connected", cnn.Username);
                    break;

                case SessionState.Disconnecting:
                    serviceEvent = ServiceEvent.Disconnecting;
                    eventDescription = String.Format("{0} is disconnecting", cnn.Username);
                    break;
            }

            if(serviceEvent != ServiceEvent.None)
            {
                Message notification = createNotificationOfEvent(serviceEvent, eventDescription);
                Broadcast(notification);
            }


            logger.LogInformation(88, "Connection state: {0}", newState);
        }
        #endregion

        #region Creating Messages
        protected Message CreateResponse(Message message, MessageType ofType = MessageType.NOT_SET)
        {
            Message response = new Message(ofType);
            response.Target = message.Sender;
            response.ResponseID = message.ID;
            response.Tag = message.Tag;

            return response;
        }

        private Message createNotificationOfEvent(ServiceEvent serviceEvent, String desc)
        {
            Message notification = new Message(MessageType.NOTIFICATION);
            notification.SubType = (int)serviceEvent;
            notification.AddValue("Description", desc);
            return notification;
        }
        #endregion

        #region Sending Messages
        protected void Broadcast(Message message)
        {
            cnn.Broadcast(message);
        }

        protected void SendMessage(Message message){
            cnn.SendMessageAsync(message);
        }
        #endregion

        #region Receiving Messages

        private bool messageReceived(Message message, Message response)
        {
            switch (message.Type)
            {
                case MessageType.SUBSCRIBE:
                    response.Type = MessageType.SUBSCRIBE_RESPONSE;
                    cnn.AddContact(message.Sender);
                    return true;

                case MessageType.PING:
                    response.Type = MessageType.PING_RESPONSE;
                    return true;

                case MessageType.ERROR_TEST:
                    response.Type = MessageType.ERROR;
                    return true;

                case MessageType.COMMAND:
                    response.Type = MessageType.COMMAND_RESPONSE;
                    String command = message.GetString("Command");
                    if(String.IsNullOrEmpty(command))
                    {
                        throw new ChetchXMPPServiceException("Command cannot be null or empty");
                    }
                    command = command.ToLower().Trim();
                    if(command.Contains(' '))
                    {
                        throw new ChetchXMPPServiceException("Command contain any spaces");
                    }
                    response.AddValue("OriginalCommand", command);
                    List<Object> args = message.GetList<Object>("Arguments");
                    
                    return CommandReceived(command, args, response);
            }
            return false;
        }


        //Command related stuff
        virtual protected bool CommandReceived(String command, List<Object> arguments, Message response)
        {
            switch (command)
            {
                case "h":
                case "help":
                    AddCommandHelp("(h)ehp", "Lists commands for this service");
                    response.AddValue("Help", commandHelp);
                    break;
            }
            return true;
        }

        virtual protected void AddCommandHelp(String command, String description)
        {
            commandHelp[command] = description;
        }
        #endregion
    }
}
