﻿using System;
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
using Microsoft.Extensions.Hosting;
using XmppDotNet.Xmpp.Muc.User;

namespace Chetch.ChetchXMPP
{
    //Command related stuff
    public class ChetchXMPPService<T>(ILogger<T> logger) : Service<T>(logger) where T : BackgroundService
    {
        #region Constants
        const int EVENT_ID_GENERICERROR = 88;
        const int EVENT_ID_CREATEXMPP = 1088;
        const int EVENT_ID_CONNECTIONERROR = 188;
        const int EVENT_ID_SESSIONSTATECHANGE = 1188;
        const int EVENT_ID_START2CONNECT = 1288;
        const int EVENT_ID_CONNECTED = 1289;
        const int EVENT_ID_MESSAGERECEIVEDERROR = 98;
        const int EVENT_ID_SENDRESPONSEERROR = 99;
        const int EVENT_ID_SUBSCRIPTION = 2010;
        const int EVENT_ID_STATUS_CHANGE = 2020;

        public const String COMMAND_HELP = "help";
        public const String COMMAND_ABOUT = "about";
        public const String COMMAND_VERSION = "version";

        public const String MESSAGE_FIELD_SERVICE_EVENT = "ServiceEvent";
        #endregion

        #region Class declarations
        protected enum ServiceEvent
        {
            None = 0, //for initialising
            Disconnected = 10000,
            Connected = 10001,
            Disconnecting = 10002,
            Stopping = 10003,
            StatusUpdate = 10004,
        }

        public class ServiceCommand
        {
            static public String Sanitize(String command)
            {
                if (command.Contains(' '))
                {
                    command = command.Replace(' ', '-');
                }
                return command.ToLower().Trim();
            }

            public String Command { get; set; } = String.Empty;
            public String Shortcut { get; set; } = String.Empty;
            public String Description { get; set; } = String.Empty;
            public bool Implemented { get; set; } = false;

            public String HelpLabel
            {
                get
                {
                    if (String.IsNullOrEmpty(Shortcut))
                    {
                        return Command;
                    }
                    else
                    {
                        int i = Command.IndexOf(Shortcut);
                        String p1 = Command.Substring(0, i);
                        String r = String.Format("({0})", Shortcut);
                        String p2 = Command.Substring(i + Shortcut.Length);
                        return p1 + r + p2;
                    }
                }
            }

            public String HelpDescription => Implemented ? Description : Description + " (not implemented)";

            public ServiceCommand(String command, String description, String shortcut, bool implemented)
            {
                command = Sanitize(command);
                if (!String.IsNullOrEmpty(shortcut))
                {
                    shortcut = Sanitize(shortcut);
                }

                if (!String.IsNullOrEmpty(shortcut) && !command.Contains(shortcut))
                {
                    throw new ArgumentException("Shortcut must be contained in command");
                }
                Command = command;
                Shortcut = shortcut;
                Description = description;
                Implemented = implemented;
            }

            public void AssertArguments(int passedArguments, int requiredArguments)
            {
                if (passedArguments != requiredArguments)
                {
                    throw new ArgumentException(String.Format("Command {0} passed {1} arguments but requires {2} arguments", Command, passedArguments, requiredArguments));
                }
            }

            public void AssertArguments(int passedArguments)
            {
                if (passedArguments == 0)
                {
                    throw new ArgumentException(String.Format("Command {0} requires at least 1 argument", Command));
                }
            }
        }
        #endregion

        #region Fields and Properties
        protected ChetchXMPPConnection Connection { get; private set; }

        SortedDictionary<String, ServiceCommand> commands = new SortedDictionary<String, ServiceCommand>();

        protected String Version { get; set; } = "!.0";
        protected String About { get; set; } = String.Format("{0} is a Chetch XMPP Service", ServiceName);
        protected int ServerTimeOffset => DateTimeOffset.Now.Offset.Hours;
        protected DateTime ServerTime => DateTime.Now;

        //Service Event relates to the underlying connectvity of the service.  This differe from the service Status
        //which is bespoke for each service and indicates a change in the particular service but not in its connectivity
        //Indeed a status change is a Service Event.
        protected event EventHandler<ServiceEvent> ServiceChanged;
        protected bool ServiceConnected => Connection != null && Connection.Ready; //for convenience

        //Status related stuff is bespoke to a particular service implementation and does not related to the general connectivity
        //and starting or stopping of a service
        int statusCode = 0;
        protected int StatusCode
        {
            get { return statusCode; }
            set
            {
                if (value != statusCode)
                {
                    logger.LogWarning(EVENT_ID_STATUS_CHANGE, "Status code changed from {0} to {1}", statusCode, value); ;
                    statusCode = value;
                    StatusChanged?.Invoke(this, statusCode);
                    ServiceChanged?.Invoke(this, ServiceEvent.StatusUpdate);

                    if (Connection != null && Connection.ReadyToSend)
                    {
                        try
                        {
                            Message notification = createNotificationOfEvent(ServiceEvent.StatusUpdate, "Status updated");
                            Broadcast(notification);
                        }
                        catch (Exception e)
                        {
                            logger.LogError(EVENT_ID_GENERICERROR, e, e.Message);
                        }
                    }
                }
            }
        }
        protected String StatusMessage { get; set; } = String.Empty;

        protected event EventHandler<int> StatusChanged;

        virtual protected Dictionary<String, Object> StatusDetails { get;  } = new Dictionary<String, Object>();
        #endregion

        #region Methods
        protected ServiceCommand AddCommand(String command, String description, String shortcut = null, bool implemented = true)
        {
            ServiceCommand cmd = new ServiceCommand(command, description, shortcut, implemented);
            if (!commands.ContainsKey(cmd.Command))
            {
                commands.Add(cmd.Command, cmd);
                return cmd;
            } else
            {
                throw new Exception(String.Format("There already exists a command {0}", cmd.Command));
            }
        }

        protected void AddCommand(String command, String description, bool implemented)
        {
            AddCommand(command, description, null, implemented);
        }

        protected ServiceCommand GetCommand(String commandOrShortcut)
        {
            commandOrShortcut = ServiceCommand.Sanitize(commandOrShortcut);

            if (commands.ContainsKey(commandOrShortcut))
            {
                return commands[commandOrShortcut];
            } else
            {
                foreach(var cmd in commands.Values)
                {
                    if (!String.IsNullOrEmpty(cmd.Shortcut) && cmd.Shortcut.Equals(commandOrShortcut))
                    {
                        return cmd;
                    }
                }
            }
            return null;
        }

        virtual protected void AddCommands()
        {
            AddCommand(COMMAND_HELP, "Lists commands for this service", "h");
            AddCommand(COMMAND_ABOUT, "Some info this service", "a");
            AddCommand(COMMAND_VERSION, "Service version", "v");
        }

        protected String DecryptPassword(String pwd, String encryption)
        {
            switch (encryption)
            {
                default:
                    return pwd;
            }
        }
        #endregion

        #region Service lifecycle
        override protected async Task Execute(CancellationToken stoppingToken)
        {
            //unnecessary wait???
            await Task.Delay(100);
            AddCommands();

            //do some config
            var config = GetAppSettings();
            Version = config.GetValue<String>("Service:Version", Version);
            About = config.GetValue<String>("Service:About", About);

            String username = config.GetValue<String>("Credentials:Username");
            String password = config.GetValue<String>("Credentials:Password");
            String encryption = config.GetValue<String>("Credentials:Encryption");
            password = DecryptPassword(password, encryption);

            //create the connection
            logger.LogInformation(EVENT_ID_CREATEXMPP, "Creating XMPP connection for user {0}...", username);
            Connection = new ChetchXMPPConnection(username, password);

            //Set event handlers
            Connection.SessionStateChanged += (sender, state) => {
                try
                {
                    handleSessionStateChanged(state);
                } catch (Exception e)
                {
                    logger.LogError(EVENT_ID_GENERICERROR, e, e.Message);
                }
            };

            Connection.MessageReceived += (sender, eargs) =>
            {
                if (eargs.Message != null)
                {
                    try
                    {
                        bool respond = false;
                        Message message = eargs.Message;
                        Message response = CreateResponse(message);
                        try
                        {
                            respond = HandleMessageReceived(message, response);
                        } catch(Exception e)
                        {
                            respond = true;
                            response = CreateError(e, message);   
                        }

                        if (respond)
                        {
                            try
                            {
                                SendMessage(response);
                            }
                            catch (Exception e)
                            {
                                logger.LogError(EVENT_ID_SENDRESPONSEERROR, e, e.Message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(EVENT_ID_MESSAGERECEIVEDERROR, ex, ex.Message);
                    }

                }
            };

            //Now connect
            Task connectTask = Connection.ConnectAsync();
            try
            {
                logger.LogInformation(EVENT_ID_START2CONNECT, "Awaiting connect process to complete...");
                await connectTask;
                logger.LogInformation(EVENT_ID_CONNECTED, "Connect process completed!");

            }
            catch (Exception e)
            {
                logger.LogError(EVENT_ID_CONNECTIONERROR, e, e.Message);
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            String eventDescription = String.Format("{0} is stopping", ServiceName);
            if (Connection != null)
            {
                Message notification = createNotificationOfEvent(ServiceEvent.Stopping, eventDescription);
                Broadcast(notification);
                Connection.DisconnectAsync();
            }

            return base.StopAsync(cancellationToken);
        }
        #endregion

        #region XMPP connection handlers
        private void handleSessionStateChanged(SessionState newState)
        {
            ServiceEvent serviceEvent = ServiceEvent.None;
            String eventDescription = String.Empty;
            switch (newState)
            {
                case SessionState.Binded:
                    serviceEvent = ServiceEvent.Connected;
                    eventDescription = String.Format("{0} is connected", Connection.Username);
                    break;

                case SessionState.Disconnecting:
                    serviceEvent = ServiceEvent.Disconnecting;
                    eventDescription = String.Format("{0} is disconnecting", Connection.Username);
                    break;

                case SessionState.Disconnected:
                    break;
            }

            if(serviceEvent != ServiceEvent.None)
            {
                ServiceChanged?.Invoke(this, serviceEvent);
                Message notification = createNotificationOfEvent(serviceEvent, eventDescription);
                Broadcast(notification);
            }


            logger.LogInformation(EVENT_ID_SESSIONSTATECHANGE, "Connection state: {0}", newState);
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


        protected Message CreateError(String errorMessage, Message originatingMessage)
        {
            Message error = new Message(MessageType.ERROR);
            if (originatingMessage != null)
            {
                error.Target = originatingMessage.Sender;
                error.ResponseID = originatingMessage.ID;
                error.Tag = originatingMessage.Tag;
            }
            error.AddValue("Message",errorMessage);
            return error;
        }
        protected Message CreateError(Exception e, Message originatingMessage)
        {
            var error = CreateError(e.Message, originatingMessage);
            error.AddValue("Exception", e.GetType().ToString());

            return error;
        }

        private Message createNotificationOfEvent(ServiceEvent serviceEvent, String desc)
        {
            Message notification = ChetchXMPPMessaging.CreateNotificationMessage((int)serviceEvent);
            notification.AddValue(MESSAGE_FIELD_SERVICE_EVENT, serviceEvent);
            notification.AddValue("Description", desc);
            switch (serviceEvent)
            {
                case ServiceEvent.StatusUpdate:
                    notification.AddValue("StatusCode", StatusCode);
                    notification.AddValue("StatusMessage", StatusMessage);
                    notification.AddValue("ServerTime", ServerTime);
                    notification.AddValue("ServerTimeOffset", ServerTimeOffset);
                    break;
            }
            return notification;
        }
        #endregion

        #region Sending Messages
        virtual protected void Broadcast(Message message)
        {
            Connection.Broadcast(message);
        }

        virtual protected void SendMessage(Message message){
            Connection.SendMessageAsync(message);
        }
        #endregion

        #region Receiving Messages

        protected virtual bool HandleMessageReceived(Message message, Message response)
        {
            switch (message.Type)
            {
                case MessageType.SUBSCRIBE:
                    response.Type = MessageType.SUBSCRIBE_RESPONSE;
                    response.AddValue("Welcome", String.Format("Welcome to {0} service", ServiceName));
                    response.AddValue("StatusCode", StatusCode);
                    response.AddValue("StatusMessage", StatusMessage);
                    response.AddValue("ServerTime", ServerTime);
                    response.AddValue("ServerTimeOffset", ServerTimeOffset);
                    Connection.AddContact(message.Sender);
                    logger.LogWarning(EVENT_ID_SUBSCRIPTION, "{0} has subscribed", message.Sender);
                    return true;

                case MessageType.STATUS_REQUEST:
                    response.Type = MessageType.STATUS_RESPONSE;
                    response.AddValue("StatusCode", StatusCode);
                    response.AddValue("StatusMessage", StatusMessage);
                    response.AddValue("StatusDetails", StatusDetails);
                    response.AddValue("ServerTime", ServerTime);
                    response.AddValue("ServerTimeOffset", ServerTimeOffset);
                    return true;

                case MessageType.PING:
                    response.Type = MessageType.PING_RESPONSE;
                    response.AddValue("StatusDetails", StatusDetails);
                    response.AddValue("ServerTime", ServerTime);
                    response.AddValue("ServerTimeOffset", ServerTimeOffset);
                    return true;

                case MessageType.ERROR_TEST:
                    response.Type = MessageType.ERROR;
                    return true;

                case MessageType.COMMAND:
                    response.Type = MessageType.COMMAND_RESPONSE;
                    String command = ChetchXMPPMessaging.GetCommandFromMessage(message);
                    ServiceCommand cmd = GetCommand(command);
                    if(cmd == null)
                    {
                        throw new ChetchXMPPServiceException(String.Format("Command {0} not found", command));
                    }
                    if (!cmd.Implemented)
                    {
                        throw new ChetchXMPPServiceException(String.Format("Command {0} not yet implemented", command));
                    }
                    //always return the full version of the command
                    response.AddValue(ChetchXMPPMessaging.MESSAGE_FIELD_ORIGINAL_COMMAND, cmd.Command);

                    //get the command arguments (returns empty list at a minimum)
                    List<Object> args = ChetchXMPPMessaging.GetArgumentsFromMessage(message);
                    
                    //pass the cmd and args to a handler
                    return HandleCommandReceived(cmd, args, response);

                case MessageType.COMMAND_RESPONSE:
                    String originalCommand = message.GetString(ChetchXMPPMessaging.MESSAGE_FIELD_ORIGINAL_COMMAND);
                    return HandleCommandResponseReceived(originalCommand, message, response);
            }
            return false;
        }

        virtual protected bool HandleCommandReceived(ServiceCommand command, List<Object> arguments, Message response)
        {
            switch (command.Command)
            {
                case COMMAND_HELP:
                    Dictionary<String, String> commandHelp = new Dictionary<String, String>();
                    foreach (var key in commands.Keys)
                    {
                        var cmd = commands[key];
                        commandHelp.Add(cmd.HelpLabel, cmd.HelpDescription);
                    }
                    response.AddValue("Help", commandHelp);
                    break;

                case COMMAND_ABOUT:
                    response.AddValue("About", About);
                    break;

                case COMMAND_VERSION:
                    response.AddValue("Version", Version);
                    break;
            }
            return true;
        }
        
        virtual protected bool HandleCommandResponseReceived(String originalCommand, Message commandResponse, Message response)
        {
            return false;
        }
        #endregion
    }
}
