using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Chetch.Messaging;
using XmppDotNet.Xmpp.Base;

namespace Chetch.ChetchXMPP
{
    public static class ChetchXMPPMessaging
    {
        #region Constants
        public const String MESSAGE_FIELD_COMMAND = "Command";
        public const String MESSAGE_FIELD_ARGUMENTS = "Arguments";

        public const String COMMAND_HELP = "help";
        public const String COMMAND_ABOUT = "about";
        public const String COMMAND_VERSION = "version";
        public const String COMMAND_STATUS = "status";
        #endregion

        static public String GetCommandFromMessage(Chetch.Messaging.Message message)
        {
            if(message.Type != MessageType.COMMAND)
            {
                throw new ChetchXMPPException(String.Format("Message is of type {0} ... it must be a command type", message.Type));
            }
            if (!message.HasValue(MESSAGE_FIELD_COMMAND))
            {
                throw new ChetchXMPPException("Command message does not have the correct command value set");
            }
            String command = message.GetString(MESSAGE_FIELD_COMMAND);
            if (String.IsNullOrEmpty(command))
            {
                throw new ChetchXMPPException("Command cannot be null or empty");
            }
            return command;
        }

        static public List<Object> GetArgumentsFromMessage(Chetch.Messaging.Message message)
        {
            if (message.Type != MessageType.COMMAND)
            {
                throw new ChetchXMPPException(String.Format("Message is of type {0} ... it must be a command type", message.Type));
            }
            if (!message.HasValue(MESSAGE_FIELD_ARGUMENTS))
            {
                return new List<Object>();
            }

            var args = message.GetList<Object>(MESSAGE_FIELD_ARGUMENTS);
            return args;
        }

        static public Chetch.Messaging.Message CreateCommandMessage(String command, params Object[] args)
        {
            if (String.IsNullOrEmpty(command))
            {
                throw new ArgumentException("ChetchXMPPService::CreateCommandMessage command param cannot be empty or null");
            }
            var message = new Chetch.Messaging.Message(MessageType.COMMAND);
            message.AddValue(MESSAGE_FIELD_COMMAND, command);

            if (args != null && args.Length > 0)
            {
                message.AddValue(MESSAGE_FIELD_ARGUMENTS, args.ToList<Object>());
            }
            return message;
        }

        static public Chetch.Messaging.Message CreateCommandMessage(String commandString)
        {
            if (String.IsNullOrEmpty(commandString))
            {
                throw new ArgumentException("ChetchXMPPService::CreateCommandMessage command string cannot be empty or null");
            }

            var message = new Chetch.Messaging.Message(MessageType.COMMAND);
            var parts = commandString.Split(' ');
            message.AddValue(MESSAGE_FIELD_COMMAND, parts[0].ToLower().Trim());
            if(parts.Length > 1)
            {
                List<String> args = new List<String>();
                for(int i = 1; i < parts.Length; i++)
                {
                    if (!String.IsNullOrEmpty(parts[i]))
                    {
                        var arg = parts[i].ToLower().Trim();
                        args.Add(arg);
                    }
                }
                if(args.Count > 0)
                {
                    message.AddValue(MESSAGE_FIELD_ARGUMENTS, args);
                }
            }

            return message;
        }
    
        static public T GetArgument<T>(int idx, List<Object> arguments, T defaultValue = default(T))
        {
            if(arguments == null || arguments.Count == 0 || idx >= arguments.Count)
            {
                return defaultValue;
            }
            var arg = arguments[idx];
            var json = JsonSerializer.Serialize(arg);
            var carg = JsonSerializer.Deserialize<T>(json);
            return carg;
        }

        static public Chetch.Messaging.Message CreateAlertMessage(int alertCode, String target = null)
        {
            var msg = new Messaging.Message(MessageType.ALERT);
            msg.SubType = alertCode;
            if(target != null)
            {
                msg.Target = target;
            }
            return msg;
        }
    }
}
