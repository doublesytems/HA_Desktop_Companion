using HADC_REBORN.Class.Actions;
using HADC_REBORN.Class.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HADC_REBORN.Class.HomeAssistant.Objects
{
    public class WsWrapper
    {
        private const int reconnectDelayMinSeconds = 5;
        private const int reconnectDelayMaxSeconds = 60;

        private YamlLoader yamlLoader;
        private WsConnector wsConnector;

        private readonly object stateLock = new object();
        private bool shouldRun = false;
        private int generation = 0;
        private System.Threading.Timer? pingTimer = null;

        public WsWrapper(YamlLoader yamlLoaderDependency, WsConnector wsConnectorDependency)
        {
            yamlLoader = yamlLoaderDependency;
            wsConnector = wsConnectorDependency;
        }

        //Connects now and throws when the server can not be reached
        public void Connect()
        {
            lock (stateLock)
            {
                if (shouldRun)
                {
                    return;
                }

                wsConnector.register();
                startWorker(true);
            }
        }

        public void Disconnect()
        {
            lock (stateLock)
            {
                shouldRun = false;
                generation++;

                if (pingTimer != null)
                {
                    pingTimer.Dispose();
                    pingTimer = null;
                    App.log.writeLine("[WS] Ping Stopped");
                }

                App.log.writeLine("[WS] Disconecting");
                wsConnector.disconnect();
                App.log.writeLine("[WS] Disconected");
            }
        }

        //Reconnects, keeps retrying in background when the server can not be reached
        public void restart()
        {
            lock (stateLock)
            {
                Disconnect();

                try
                {
                    wsConnector.register();
                    startWorker(true);
                }
                catch (Exception ex)
                {
                    App.log.writeLine("[WS] Reconnect failed, retrying in background: " + ex.Message);
                    startWorker(false);
                }
            }
        }

        //Caller must hold stateLock
        private void startWorker(bool registered)
        {
            shouldRun = true;
            int workerGeneration = ++generation;

            Thread worker = new Thread(() => workerLoop(workerGeneration, registered));
            worker.IsBackground = true;
            worker.Name = "HADC WebSocket";
            worker.Start();

            if (pingTimer != null)
            {
                pingTimer.Dispose();
            }
            pingTimer = new System.Threading.Timer(pingTimer_Tick, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

            App.log.writeLine("[WS] Ping Initialized");
        }

        private bool isCurrentWorker(int workerGeneration)
        {
            lock (stateLock)
            {
                return shouldRun && workerGeneration == generation;
            }
        }

        //Receives messages and reconnects when the connection is lost
        private void workerLoop(int workerGeneration, bool registered)
        {
            int delaySeconds = reconnectDelayMinSeconds;

            while (true)
            {
                if (registered)
                {
                    delaySeconds = reconnectDelayMinSeconds;

                    try
                    {
                        wsConnector.receive(eventReceive);
                    }
                    catch (Exception ex)
                    {
                        App.log.writeLine("[WS] Receive failed: " + ex.Message);
                    }

                    if (!isCurrentWorker(workerGeneration))
                    {
                        return;
                    }

                    App.log.writeLine("[WS] Connection lost, reconnecting");
                    registered = false;
                }

                Thread.Sleep(TimeSpan.FromSeconds(delaySeconds));
                delaySeconds = Math.Min(delaySeconds * 2, reconnectDelayMaxSeconds);

                lock (stateLock)
                {
                    if (!shouldRun || workerGeneration != generation)
                    {
                        return;
                    }

                    try
                    {
                        wsConnector.register();
                        registered = true;
                        App.log.writeLine("[WS] Reconnected");
                    }
                    catch (Exception ex)
                    {
                        App.log.writeLine("[WS] Reconnect failed: " + ex.Message);
                    }
                }
            }
        }

        private void pingTimer_Tick(object? state)
        {
            if (!wsConnector.connected())
            {
                return;
            }

            try
            {
                wsConnector.ping();
            }
            catch (Exception ex)
            {
                App.log.writeLine("[WS] Ping Failed: " + ex.Message);

                //Closing the dead connection makes the worker reconnect
                wsConnector.disconnect();
            }
        }

        public static void eventReceive(JObject eventData)
        {
            if (!eventData.ContainsKey("event"))
            {
                return;
            }

            JObject? eventPayloadData = eventData["event"] as JObject;
            if (eventPayloadData == null)
            {
                return;
            }

            JObject? eventPayloadExtraData = eventPayloadData["data"] as JObject;

            if (eventPayloadData.ContainsKey("message"))
            {
                App.log.writeLine("[WS] Event With Message");

                string msg_text = eventPayloadData["message"].ToString();
                string msg_title = "";
                string msg_image = "";
                string msg_audio = "";

                if (eventPayloadData.ContainsKey("title"))
                {
                    msg_title = eventPayloadData["title"].ToString();
                }

                if (eventPayloadExtraData != null && eventPayloadExtraData.ContainsKey("image"))
                {
                    msg_image = eventPayloadExtraData["image"].ToString();
                }

                if (eventPayloadExtraData != null && eventPayloadExtraData.ContainsKey("audio"))
                {
                    msg_audio = eventPayloadExtraData["audio"].ToString();
                }

                Notification.Spawn(msg_text, msg_title, msg_image, msg_audio);
            }

            if (eventPayloadExtraData != null && eventPayloadExtraData.ContainsKey("key"))
            {
                Keyboard.SendKey(eventPayloadExtraData["key"].ToString());
            }
        }
    }
}
