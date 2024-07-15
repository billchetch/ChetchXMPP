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

namespace Chetch.ChetchXMPP
{
    public class ChetchXMPPService(ILogger<ChetchXMPPService> logger) : Service<ChetchXMPPService>(logger)
    {

        override protected async Task Execute(CancellationToken stoppingToken)
        {
            await Task.Delay(100);

            var config = getAppSettings();
            String username = config.GetValue<String>("Credentials:Username");
            String password = config.GetValue<String>("Credentials:Password");
            logger.LogInformation(88, "Creating XMPP connection for user {0}...", username);


            var cnn = new ChetchXMPPConnection(username, password);
            cnn.SessionStateChanged += (sender, state) => {
                logger.LogInformation(88, "Connection state: {0}", state);
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
                            cnn.SendMessageAsync(response);
                        }
                        catch (Exception e)
                        {
                            logger.LogError(1288, e, e.Message);
                        }
                    }
                }
            };

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

        protected Message CreateResponse(Message message, MessageType ofType = MessageType.NOT_SET)
        {
            Message response = new Message(ofType);
            response.Target = message.Sender;
            response.ResponseID = message.ID;
            response.Tag = message.Tag;

            return response;
        }

        private bool messageReceived(Message message, Message response)
        {
            switch (message.Type)
            {
                case MessageType.PING:
                    response.Type = MessageType.PING_RESPONSE;
                    return true;

                case MessageType.ERROR_TEST:
                    response.Type = MessageType.ERROR;
                    return true;

                case MessageType.COMMAND:
                    response.Type = MessageType.COMMAND_RESPONSE;
                    String command = message.GetString("Command");
                    response.AddValue("OriginalCommand", command);
                    List<Object> args = message.GetList<Object>("Arguments");
                    return CommandReceived(command, args, response);
            }
            return false;
        }

        virtual protected bool CommandReceived(String command, List<Object> arguments, Message response)
        {
            return true;
        }
    }
}
