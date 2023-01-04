using System;
using System.Text;
using System.IO;
using TwinCAT.Ads;
using System.Configuration;
using System.Windows.Forms;
using System.Threading;

namespace DeteleFiles
{
    class Program
    {
        private static NotifyIcon notifyIcon;
        private static AmsAddress serverAddress;
        private static TcAdsClient tcAdsClient = null;
        private static string startVar;
        private static string recordVar;
        private static string storagePath;
        private static int periodOfValidity;
        private static string amsId;
        private static string fileName;
        private static int portNo;
        private static ConnectionStatus connectionStatus = ConnectionStatus.CheckCfg;
        private static bool startVarResult = false;
        private static string recordVarResult = "";
        [Obsolete]
        static void Main(string[] args)
        {
            startVar = ConfigurationSettings.AppSettings["Start_variable"];
            recordVar = ConfigurationSettings.AppSettings["Record_varibale"];
            storagePath = ConfigurationSettings.AppSettings["Storage_path"];
            periodOfValidity = Convert.ToInt32(ConfigurationSettings.AppSettings["Period_of_validity"]);
            amsId = ConfigurationSettings.AppSettings["AmsId"];
            portNo = Convert.ToInt32(ConfigurationSettings.AppSettings["PortNo"]);
            fileName = ConfigurationSettings.AppSettings["FileName"];
            StateInfo stateInfo;

            int startVarHandle = 0;
            int recordVarHandle = 0;

            notifyIcon = new NotifyIcon();
            notifyIcon.BalloonTipText = "Data recorder";
            notifyIcon.Text = "Data recorder";
            notifyIcon.Icon = new System.Drawing.Icon("DataRecorder.ico");
            notifyIcon.Visible = true;
            while (true)
            {
               switch (connectionStatus)
                {
                  //Check configuration
                    case ConnectionStatus.CheckCfg:
                        try
                        {
                            if (amsId.ToUpper() == "LOCAL")
                            {
                                serverAddress = new AmsAddress(TwinCAT.Ads.AmsNetId.Local, portNo);
                            }else
                            {
                                serverAddress = new AmsAddress(amsId, portNo);
                            }
                            connectionStatus = ConnectionStatus.Disconnect;
                        }
                        catch
                        {
                            MessageBox.Show("Invalid AMS NetId or Ams port");
                            return;
                        }
                        break;
                  //Disconnect wait connect
                    case ConnectionStatus.Disconnect:
                        try
                        {
                            tcAdsClient = new TcAdsClient();
                            tcAdsClient.Connect(serverAddress.NetId, serverAddress.Port);
                            connectionStatus = ConnectionStatus.ReadStatus;
                        }
                        catch
                        {
                            DialogResult dialogResult;
                            dialogResult = MessageBox.Show("Could not connect ads client, " + serverAddress.NetId + " : " + serverAddress.Port.ToString(),
                                "Data recorder", MessageBoxButtons.RetryCancel);
                            if (dialogResult == DialogResult.Cancel)
                            {
                                return;
                            }else
                            {
                                Thread.Sleep(2000);
                            }
                        }
                        break;
                    //Read ads status
                    case ConnectionStatus.ReadStatus:
                        try
                        {
                            stateInfo = tcAdsClient.ReadState();
                            if (stateInfo.AdsState != AdsState.Invalid &&
                                stateInfo.AdsState != AdsState.Exception &&
                                stateInfo.AdsState != AdsState.Error &&
                                stateInfo.AdsState != AdsState.Shutdown &&
                                stateInfo.AdsState != AdsState.Stopping && 
                                stateInfo.AdsState != AdsState.Stop &&
                                stateInfo.AdsState != AdsState.PowerFailure)
                            {
                                connectionStatus = ConnectionStatus.CreateHandler;
                            }
                        }
                        catch(AdsErrorException e)
                        {
                            if (e.ErrorCode == AdsErrorCode.PortNotConnected ||
                                e.ErrorCode == AdsErrorCode.TargetPortNotFound ||
                                e.ErrorCode == AdsErrorCode.PortDisabled)
                            {
                                Thread.Sleep(1000);
                            }
                            else
                                connectionStatus = ConnectionStatus.Disconnect;
                        }
                        catch
                        { connectionStatus = ConnectionStatus.Disconnect; }
                        break;
                  //Create variable handler
                    case ConnectionStatus.CreateHandler:
                        try
                        {
                            startVarHandle = tcAdsClient.CreateVariableHandle(startVar);
                            recordVarHandle = tcAdsClient.CreateVariableHandle(recordVar);
                            connectionStatus = ConnectionStatus.Connected;
                        }
                        catch
                        {
                            DialogResult dialogResult;
                            dialogResult = MessageBox.Show("Can not find Plc variable = Bool variable :（" + startVar + " ) + String variable: （" + recordVar + ")",
                                "Data recorder", MessageBoxButtons.RetryCancel);

                            if (dialogResult == DialogResult.Cancel)
                            {
                                return;
                            }else
                            {
                                connectionStatus = ConnectionStatus.ReadStatus;
                            }
                        }
                        break;
                    //Delete handler
                    case ConnectionStatus.DeleteHandler:
                        try
                        {
                            if (startVarHandle != 0)
                            {
                                tcAdsClient.DeleteVariableHandle(startVarHandle);
                            }
                            if (recordVarHandle != 0)
                            {
                                tcAdsClient.DeleteVariableHandle(recordVarHandle);
                            }
                            connectionStatus = ConnectionStatus.CreateHandler;
                        }
                        catch (AdsErrorException e)
                        {
                            if (e.ErrorCode == AdsErrorCode.DeviceSymbolNotFound)
                            {
                                connectionStatus = ConnectionStatus.CreateHandler;
                            }
                        }
                        catch
                        {
                            connectionStatus = ConnectionStatus.Disconnect;
                        }
                            break;
                  //Start Communication
                    case ConnectionStatus.Connected:
                        try
                        {
                            stateInfo = tcAdsClient.ReadState();
                            if (stateInfo.AdsState == AdsState.Run)
                            {
                                startVarResult = (bool)tcAdsClient.ReadAny(startVarHandle, typeof(bool));
                                recordVarResult = tcAdsClient.ReadAnyString(recordVarHandle,1000,Encoding.Default);
                                if (startVarResult)
                                {
                                    SaveFile();
                                    tcAdsClient.WriteAny(startVarHandle, false);
                                }
                            }
                        }
                        catch(AdsErrorException e)
                        {
                            if (e.ErrorCode == AdsErrorCode.PortNotConnected ||
                                e.ErrorCode == AdsErrorCode.TargetPortNotFound ||
                                e.ErrorCode == AdsErrorCode.PortDisabled)
                            {
                                connectionStatus = ConnectionStatus.ReadStatus;
                            }
                            else if (e.ErrorCode == AdsErrorCode.DeviceInvalidOffset ||
                                e.ErrorCode == AdsErrorCode.DeviceNotifyHandleInvalid ||
                                e.ErrorCode == AdsErrorCode.DeviceSymbolNotFound)
                            {
                                connectionStatus = ConnectionStatus.DeleteHandler;
                            }else
                            {
                                connectionStatus = ConnectionStatus.Disconnect;
                            }
                        }
                        catch
                        { connectionStatus = ConnectionStatus.Disconnect; }
                        break;
                }
            }
        }


        private static void SaveFile()
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(storagePath);
            try
            {
                var subDirs = directoryInfo.GetDirectories();
                TimeSpan compareTime = new TimeSpan(periodOfValidity * 30,0,0,0);
                var dirExist = false;
                var fileExist = false;
                foreach (var subDir in subDirs)
                {
                    if (System.DateTime.Now - subDir.CreationTime > compareTime &&
                        subDir.Name.Contains(fileName))
                    {
                        subDir.Delete(true);
                    }
                    else if (subDir.CreationTime.Month == System.DateTime.Now.Month &&
                        subDir.Name.Contains(fileName))
                    {
                        dirExist = true;
                        var dirFiles = subDir.GetFiles();
                        foreach (var dirFile in dirFiles)
                        {
                            if (dirFile.CreationTime.Day == System.DateTime.Now.Day &&
                                dirFile.Extension == ".CSV" &&
                                dirFile.Name.Contains(fileName))
                            {
                                fileExist = true;
                                var sw = dirFile.AppendText();
                                sw.WriteLine(System.DateTime.Now.ToString("G") + "," + recordVarResult);
                                sw.Close();
                                break;
                            }
                        }
                        if (!fileExist)
                        {
                            FileInfo fileInfo = new FileInfo(subDir.FullName + "\\" + System.DateTime.Now.ToString("yyyy-MM-dd-") + fileName + ".CSV");
                            var sw = fileInfo.CreateText();
                            sw.WriteLine(System.DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "," + recordVarResult);
                            sw.Close();
                        }
                    }
                }
                if (!dirExist)
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(directoryInfo.FullName + "\\" + System.DateTime.Now.ToString("yyyy-MM_") + fileName);
                    dirInfo.Create();
                    FileInfo fileInfo = new FileInfo(dirInfo.FullName + "\\" + System.DateTime.Now.ToString("yyyy-MM-dd-") + fileName + ".CSV");
                    var sw = fileInfo.CreateText();
                    sw.WriteLine(System.DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "," + recordVarResult);
                    sw.Close();
                }
            }
            catch(Exception e)
            {
                MessageBox.Show(e.ToString());
                return;
            }
        }
 
        enum ConnectionStatus
        {
            CheckCfg,
            Disconnect,
            ReadStatus,
            CreateHandler,
            DeleteHandler,
            Connected
        }
    }
}
