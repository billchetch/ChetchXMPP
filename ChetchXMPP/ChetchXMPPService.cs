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
using Microsoft.Extensions.Hosting;

namespace Chetch.ChetchXMPP
{
    public class ChetchXMPPService<T>(ILogger<T> logger) : Service<T>(logger) where T : BackgroundService
    {
        #region Constants
        const String COMMAND_HELP = "help";
        const String COMMAND_ABOUT = "about";
        const String COMMAND_VERSION = "version";
        const String COMMAND_STATUS = "status";

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

        #endregion

        #region Class declarations
        enum ServiceEvent
        {
            None = 0, //for initialising
            Connected = 1,
            Disconnecting = 2,
            Stopping = 3,
            StatusUpdate = 4,
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
        }
        #endregion

        #region Fields and Properties
        ChetchXMPPConnection cnn;
        SortedDictionary<String, ServiceCommand> commands = new SortedDictionary<String, ServiceCommand>();

        protected String Version { get; set; } = "!.0";
        protected String About { get; set; } = String.Format("{0} is a Chetch XMPP Service", ServiceName);
        protected long ServerTimeInMillis => DateTimeOffset.Now.ToUnixTimeMilliseconds();

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
                    if (cnn != null && cnn.ReadyToSend)
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
            AddCommand(COMMAND_STATUS, "Status of this service", "s");
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
            cnn = new ChetchXMPPConnection(username, password);
            
            //Set event handlers
            cnn.SessionStateChanged += (sender, state) => {
                try
                {
                    handleSessionStateChanged(state);
                } catch (Exception e)
                {
                    logger.LogError(EVENT_ID_GENERICERROR, e, e.Message);
                }
            };

            cnn.MessageReceived += (sender, eargs) =>
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
                            respond = handleMessageReceived(message, response);
                        } catch(ChetchXMPPException e)
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
            Task connectTask = cnn.ConnectAsync();
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
            String eventDescription = String.Format("{0} is stopping", cnn.Username);
            Message notification = createNotificationOfEvent(ServiceEvent.Stopping, eventDescription);
            Broadcast(notification);
            cnn.DisconnectAsync();

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

            return error; ;
        }

        private Message createNotificationOfEvent(ServiceEvent serviceEvent, String desc)
        {
            Message notification = new Message(MessageType.NOTIFICATION);
            notification.SubType = (int)serviceEvent;
            notification.AddValue("Description", desc);
            switch (serviceEvent)
            {
                case ServiceEvent.StatusUpdate:
                    notification.AddValue("StatusCode", StatusCode);
                    notification.AddValue("StatusMessage", StatusMessage);
                    notification.AddValue("StatusDetails", StatusDetails);
                    notification.AddValue("ServerTimeInMillis", ServerTimeInMillis);
                    break;
            }
            return notification;
        }
        #endregion

        #region Sending Messages
        virtual protected void Broadcast(Message message)
        {
            cnn.Broadcast(message);
        }

        virtual protected void SendMessage(Message message){
            cnn.SendMessageAsync(message);
        }
        #endregion

        #region Receiving Messages

        private bool handleMessageReceived(Message message, Message response)
        {
            switch (message.Type)
            {
                case MessageType.SUBSCRIBE:
                    response.Type = MessageType.SUBSCRIBE_RESPONSE;
                    response.AddValue("Welcome", String.Format("Welcome to {0} service", ServiceName));
                    response.AddValue("StatusCode", StatusCode);
                    response.AddValue("StatusMessage", StatusMessage);
                    response.AddValue("ServerTimeInMillis", ServerTimeInMillis);
                    cnn.AddContact(message.Sender);
                    logger.LogWarning(EVENT_ID_SUBSCRIPTION, "{0} has subscribed", message.Sender);
                    return true;

                case MessageType.STATUS_REQUEST:
                    response.Type = MessageType.STATUS_RESPONSE;
                    response.AddValue("StatusCode", StatusCode);
                    response.AddValue("StatusMessage", StatusMessage);
                    response.AddValue("StatusDetails", StatusDetails);
                    response.AddValue("ServerTimeInMillis", ServerTimeInMillis);
                    return true;

                case MessageType.PING:
                    response.Type = MessageType.PING_RESPONSE;
                    response.AddValue("ServerTimeInMillis", ServerTimeInMillis);
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
                    response.AddValue("OriginalCommand", cmd.Command);

                    //get the command arguments or use an empty list if there are none
                    List<Object> args;
                    if (message.HasValue("Arguments")) 
                    {
                        args = message.GetList<Object>("Arguments");
                    } else
                    {
                        args = new List<Object>();
                    }
                    
                    return HandleCommandReceived(cmd, args, response);

                case MessageType.ALERT:
                    return HandleAlertReceived(message, message.SubType, response);
            }
            return false;
        }

        //Command related stuff
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

                case COMMAND_STATUS:
                    response.AddValue("StatusCode", StatusCode);
                    response.AddValue("StatusMessage", StatusMessage);
                    response.AddValue("StatusDetails", StatusDetails);
                    response.AddValue("ServerTimeInMillis", ServerTimeInMillis);
                    break;
            }
            return true;
        }
        
        //Alert related stuff
        virtual protected bool HandleAlertReceived(Message alert, int serverity, Message response)
        {
            return false;
        }
        #endregion
    }
}
