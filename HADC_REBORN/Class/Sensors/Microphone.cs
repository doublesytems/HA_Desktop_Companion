using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace HADC_REBORN.Class.Sensors
{
    public class MicrophoneInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class MicrophoneStatus
    {
        public bool Connected = false;
        public string Name = "";
        public bool InUse = false;
        public bool Muted = false;
        public int Volume = 0;
    }

    //Sensors for one microphone, platform "microphone" in configuration.yaml
    public class Microphone
    {
        //Id of the microphone picked in the settings, empty follows the Windows default microphone
        public static string selectedDevice = "";

        public static string GetValue(string selector, string device = "")
        {
            MicrophoneStatus status = getStatus(String.IsNullOrEmpty(device) ? selectedDevice : device);

            switch (selector)
            {
                case "in_use":
                    //A microphone that is not connected is not in use
                    return (status.Connected && status.InUse).ToString();
                case "muted":
                    requireConnected(status);
                    return status.Muted.ToString();
                case "volume":
                    requireConnected(status);
                    return status.Volume.ToString(CultureInfo.InvariantCulture);
                default:
                    throw new Exception("Unknown microphone selector '" + selector + "'");
            }
        }

        private static void requireConnected(MicrophoneStatus status)
        {
            if (!status.Connected)
            {
                throw new Exception("Microphone is not connected");
            }
        }

        //All microphones that are connected and enabled
        public static List<MicrophoneInfo> list()
        {
            List<MicrophoneInfo> microphones = new List<MicrophoneInfo>();

            using (MMDeviceEnumerator enumerator = new MMDeviceEnumerator())
            {
                foreach (MMDevice microphone in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                {
                    using (microphone)
                    {
                        microphones.Add(new MicrophoneInfo() { Id = microphone.ID, Name = microphone.FriendlyName });
                    }
                }
            }

            return microphones;
        }

        public static MicrophoneStatus getStatus(string deviceId)
        {
            MicrophoneStatus status = new MicrophoneStatus();

            using (MMDeviceEnumerator enumerator = new MMDeviceEnumerator())
            {
                MMDevice? microphone = open(enumerator, deviceId);
                if (microphone == null)
                {
                    return status;
                }

                using (microphone)
                {
                    status.Connected = true;
                    status.Name = microphone.FriendlyName;
                    status.Muted = microphone.AudioEndpointVolume.Mute;
                    status.Volume = (int)Math.Round(microphone.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                    status.InUse = hasActiveSession(microphone);
                }
            }

            return status;
        }

        private static MMDevice? open(MMDeviceEnumerator enumerator, string deviceId)
        {
            try
            {
                if (String.IsNullOrEmpty(deviceId))
                {
                    return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                }

                MMDevice microphone = enumerator.GetDevice(deviceId);
                if (microphone.State != DeviceState.Active)
                {
                    microphone.Dispose();
                    return null;
                }

                return microphone;
            }
            catch (Exception)
            {
                //Unplugged, removed or no microphone at all
                return null;
            }
        }

        //An app that records from this microphone has an active audio session on it
        private static bool hasActiveSession(MMDevice microphone)
        {
            AudioSessionManager manager = microphone.AudioSessionManager;
            manager.RefreshSessions();

            SessionCollection sessions = manager.Sessions;
            bool active = false;

            for (int i = 0; i < sessions.Count; i++)
            {
                using (AudioSessionControl session = sessions[i])
                {
                    if (session.State == AudioSessionState.AudioSessionStateActive)
                    {
                        active = true;
                    }
                }
            }

            return active;
        }
    }
}
