using HADC_REBORN.Class.HomeAssistant.Objects;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Net.WebSockets;

namespace HADC_REBORN.Class.HomeAssistant
{
    public class WsConnector
    {
        private string url;
        private string token;
        private string webhook;

        private ClientWebSocket? socket = null;
        private readonly object sendLock = new object();
        private int interactions = 1;

        private bool isConnected = false;

        public WsConnector(string apiUrl, string apiToken, string webhookId)
        {
            url = apiUrl.TrimEnd('/');
            token = apiToken;
            webhook = webhookId;
        }

        public void register()
        {
            //Drop any previous connection before opening a new one
            disconnect();

            Uri wsAddress = new Uri(url + "/api/websocket");
            ClientWebSocket newSocket = new ClientWebSocket();
            newSocket.Options.KeepAliveInterval = TimeSpan.Zero;

            try
            {
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    newSocket.ConnectAsync(wsAddress, timeout.Token).GetAwaiter().GetResult();
                    App.log.writeLine("[WS] ADDRESS:" + wsAddress);

                    JObject initialization = receiveJson(newSocket, timeout.Token);
                    if (initialization["type"]?.ToString() != "auth_required")
                    {
                        throw new Exception("Server dont asked for authorization");
                    }

                    WsAuth authObj = new WsAuth() { };
                    authObj.access_token = token;

                    send(newSocket, authObj, timeout.Token);
                    JObject registration = receiveJson(newSocket, timeout.Token);
                    if (registration["type"]?.ToString() != "auth_ok")
                    {
                        throw new Exception("Server dont accepted authorization");
                    }

                    JObject subscription;
                    lock (sendLock)
                    {
                        WsRequest subscribeObj = new WsRequest { };
                        subscribeObj.id = interactions;
                        subscribeObj.webhook_id = webhook;
                        subscribeObj.type = "mobile_app/push_notification_channel";

                        send(newSocket, subscribeObj, timeout.Token);
                        subscription = receiveJson(newSocket, timeout.Token);
                    }

                    if (subscription["success"]?.ToObject<bool>() != true)
                    {
                        throw new Exception("Server authorization failed");
                    }
                }
            }
            catch
            {
                newSocket.Dispose();
                throw;
            }

            socket = newSocket;
            isConnected = true;
        }

        //Blocks until the connection is closed or fails
        public void receive(Action<JObject> callbackRecieveEvent)
        {
            ClientWebSocket? localSocket = socket;
            if (localSocket == null)
            {
                return;
            }

            while (true)
            {
                string? jsonPayload;
                try
                {
                    jsonPayload = readMessage(localSocket, CancellationToken.None);
                }
                catch (Exception e)
                {
                    App.log.writeLine("[WS] ERROR: " + e.Message);
                    return;
                }

                if (jsonPayload == null)
                {
                    App.log.writeLine("[WS] Connection closed by server");
                    return;
                }

#if DEBUG
                App.log.writeLine("[WS] Recieved in LOOP: " + jsonPayload);
#endif

                //A message that fails to process must not end the connection
                try
                {
                    if (callbackRecieveEvent != null)
                    {
                        callbackRecieveEvent.Invoke(JObject.Parse(jsonPayload));
                    }
                }
                catch (Exception e)
                {
                    App.log.writeLine("[WS] Failed to process message: " + e.Message);
                }
            }
        }

        public void ping()
        {
            ClientWebSocket? localSocket = socket;
            if (localSocket == null)
            {
                throw new Exception("Not connected");
            }

            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                lock (sendLock)
                {
                    WsPing pingObj = new WsPing { };
                    pingObj.type = "ping";
                    pingObj.id = interactions;
                    send(localSocket, pingObj, timeout.Token);
                }
            }
        }

        public void disconnect()
        {
            isConnected = false;

            ClientWebSocket? oldSocket = Interlocked.Exchange(ref socket, null);
            if (oldSocket == null)
            {
                return;
            }

            try
            {
                //Also answer a close that was started by the server
                if (oldSocket.State == WebSocketState.Open || oldSocket.State == WebSocketState.CloseReceived)
                {
                    oldSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).Wait(2000);
                }
            }
            catch (Exception)
            {
                //Connection is already gone
            }

            try
            {
                oldSocket.Dispose();
            }
            catch (Exception)
            {
            }
        }

        public bool connected()
        {
            ClientWebSocket? localSocket = socket;
            return (isConnected && localSocket != null && localSocket.State == WebSocketState.Open);
        }

        private static string? readMessage(ClientWebSocket localSocket, CancellationToken cancellationToken)
        {
            byte[] localBuffer = new byte[2048];
            using (var ms = new MemoryStream())
            {
                WebSocketReceiveResult localResult;
                do
                {
                    localResult = localSocket.ReceiveAsync(new ArraySegment<byte>(localBuffer), cancellationToken).GetAwaiter().GetResult();
                    if (localResult.MessageType == WebSocketMessageType.Close)
                    {
                        return null;
                    }

                    ms.Write(localBuffer, 0, localResult.Count);
                } while (!localResult.EndOfMessage);

                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static JObject receiveJson(ClientWebSocket localSocket, CancellationToken cancellationToken)
        {
            string? JSONRecievedpayload = readMessage(localSocket, cancellationToken);
            if (JSONRecievedpayload == null)
            {
                throw new Exception("Server closed the connection");
            }

#if DEBUG
            App.log.writeLine("[WS] RECIEVED:");
            App.log.writeLine(JSONRecievedpayload);
#endif

            return JObject.Parse(JSONRecievedpayload);
        }

        private void send(ClientWebSocket localSocket, object payloadObj, CancellationToken cancellationToken)
        {
            string JSONPayload = JsonConvert.SerializeObject(payloadObj, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }).ToString();

#if DEBUG
            App.log.writeLine("[WS] SEND:");
            App.log.writeLine(JSONPayload);
#endif

            ArraySegment<byte> BYTEPayload = new ArraySegment<byte>(Encoding.UTF8.GetBytes(JSONPayload));
            lock (sendLock)
            {
                localSocket.SendAsync(BYTEPayload, WebSocketMessageType.Text, true, cancellationToken).GetAwaiter().GetResult();
                interactions = interactions + 1;
            }
        }
    }
}
