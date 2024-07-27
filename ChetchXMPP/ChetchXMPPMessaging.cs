using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        static public Chetch.Messaging.Message CreateCommandMessage(String commandAndArgs)
        {
            if (String.IsNullOrEmpty(commandAndArgs))
            {
                throw new ArgumentException("ChetchXMPPService::CreateCommandMessage commandAndArgs param cannot be empty or null");
            }
            var message = new Chetch.Messaging.Message(MessageType.COMMAND);
            var parts = commandAndArgs.Split(' ');
            String command = parts[0];
            message.AddValue(MESSAGE_FIELD_COMMAND, command);

            List<String> args = new List<String>();
            for (int i = 1; i < parts.Length; i++)
            {
                if (!String.IsNullOrEmpty(parts[i]))
                {
                    args.Add(parts[i].Trim());
                }
            }
            message.AddValue(MESSAGE_FIELD_ARGUMENTS, args);
            return message;
        }
    }
}
