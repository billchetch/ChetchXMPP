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

        #endregion

        #region Class declarations
        enum ServiceEvent
        {
            None = 0, //for initialising
            Connected = 1,
            Disconnecting = 2,
            Stopping = 3,
        }

        protected class ServiceCommand
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
                    int i = Command.IndexOf(Shortcut);
                    String p1 = Command.Substring(0, i);
                    String r = String.Format("({0})", Shortcut);
                    String p2 = Command.Substring(i + Shortcut.Length);
                    return p1 + r + p2;
                }
            }

            public String HelpDescription => Implemented ? Description : Description + " (not implemented)";
            
            public ServiceCommand(String command, String description, String shortcut, bool implemented)
            {
                command = Sanitize(command);
                if(shortcut == null)
                {
                    shortcut = command[0].ToString();
                } else
                {
                    shortcut = Sanitize(shortcut);
                }

                if (!command.Contains(shortcut))
                {
                    throw new ArgumentException("Shortcut must be contained in command");
                }
                Command = command;
                Shortcut = shortcut;
                Description = description;
                Implemented = implemented;
            }

            
        }
        #endregion

        #region Fields
        ChetchXMPPConnection cnn;
        SortedDictionary<String, ServiceCommand> commands = new SortedDictionary<String, ServiceCommand>();
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
                    if (cmd.Shortcut.Equals(commandOrShortcut))
                    {
                        return cmd;
                    }
                }
            }
            return null;
        }

        #endregion

        #region Service lifecycle
        override protected async Task Execute(CancellationToken stoppingToken)
        {
            //unnecessary wait???
            await Task.Delay(100);
            AddCommand("help", "Lists commands for this service");
            AddCommand("about", "Some info this service", false);
            AddCommand("version", "Service version", false);

            //do some config
            var config = getAppSettings();
            String username = config.GetValue<String>("Credentials:Username");
            String password = config.GetValue<String>("Credentials:Password");
            String encryption = config.GetValue<String>("Credentials:Encryption");
            switch (encryption?.ToLower())
            {
                case "default":
                    break;

                default:
                    //do nothing
                    break;
            }
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
                    ServiceCommand cmd = GetCommand(command);
                    if(cmd == null)
                    {
                        throw new ChetchXMPPServiceException("Command not found");
                    }
                    if (!cmd.Implemented)
                    {
                        throw new ChetchXMPPServiceException("Command not yet implemented");
                    }
                    response.AddValue("OriginalCommand", command);
                    List<Object> args = message.GetList<Object>("Arguments");
                    
                    return CommandReceived(cmd, args, response);
            }
            return false;
        }


        //Command related stuff
        virtual protected bool CommandReceived(ServiceCommand command, List<Object> arguments, Message response)
        {
            switch (command.Command)
            {
                case COMMAND_HELP:
                    Dictionary<String, String> commandHelp = new Dictionary<String, String>();
                    foreach (var cmd in commands.Values)
                    {
                        commandHelp.Add(cmd.HelpLabel, cmd.HelpDescription);
                    }
                    response.AddValue("Help", commandHelp);
                    break;
            }
            return true;
        }
        #endregion
    }
}
