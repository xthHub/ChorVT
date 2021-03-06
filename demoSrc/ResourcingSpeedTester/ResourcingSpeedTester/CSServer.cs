using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace ResourcingSpeedTester
{
    /// <summary>
    /// Http服务器类
    /// </summary>
    internal class CSServer
    {
        /// <summary>
        /// 获取或设置该Http服务的网关地址
        /// </summary>
        public string HttpServerGateway { get; set; } = "http://localhost:10301/";

        /// <summary>
        /// 开始异步接受Http请求
        /// </summary>
        public void BeginAsyncAccept()
        {
            HttpListener httpListener = new HttpListener();
            httpListener.Prefixes.Add(this.HttpServerGateway);
            httpListener.Start();
            httpListener.BeginGetContext(new AsyncCallback(this.OnGetContext), httpListener);
        }

        /// <summary>
        /// 回调函数：接受到请求时
        /// </summary>
        /// <param name="ar">异步回传的上下文</param>
        private void OnGetContext(IAsyncResult ar)
        {
            HttpListener httpListener = ar.AsyncState as HttpListener;
            HttpListenerContext context = httpListener.EndGetContext(ar);
            httpListener.BeginGetContext(new AsyncCallback(OnGetContext), httpListener);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Http Request from Gateway with Header:");
            foreach (string key in context.Request.Headers.AllKeys)
            {
                var values = context.Request.Headers.GetValues(key);
                if (values?.Length > 0)
                {
                    string s = values.Aggregate("", (current, value) => current + (value + ";"));
                    sb.AppendLine("  " + key + " : " + s);
                }
            }
            if (String.Compare(context.Request.HttpMethod, "POST") == 0)
            {
                this.OnPost(context);
            }
        }

        /// <summary>
        /// Post方法
        /// </summary>
        /// <param name="context">HTTP报文上下文</param>
        public void OnPost(HttpListenerContext context)
        {
            var postDataHelper = new HttpListenerPostParaHelper(context);
            List<HttpListenerPostValue> postParams = postDataHelper.GetHttpListenerPostValue();
            var pDict = new Dictionary<String, String>();
            foreach (var pp in postParams)
            {
                pDict[pp.Key] = pp.ValueString;
            }
            switch (pDict["TaskName"])
            {
                case "addItemTask":
                    StartAndComplete(GlobalContext.WorkerId, pDict["Wid"], null, null);
                    lock (this)
                    {
                        Console.WriteLine("Recieved Workitem " + (NetClient.Counter + 1) + ": " + pDict["Wid"] + " " + DateTime.Now);
                        NetClient.Counter++;
                    }
                    if (NetClient.Counter == 100)
                    {
                        NetClient.EndTimestamp = DateTime.Now;
                        double timeSpan = (NetClient.EndTimestamp - NetClient.BeginTimestamp).TotalSeconds;
                        String report = String.Format("请求数量：100{0}耗时：{1}秒{0}速度：{2:F4}任务/秒{0}",
                            Environment.NewLine, timeSpan, (100.0 / timeSpan));
                        Console.WriteLine(report);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("Test Finished, RTID: " + NetClient.rtid);
                        Console.ForegroundColor = ConsoleColor.White;
                        MessageBox.Show(report);
                        NetClient.Counter = 0;
                    }
                    break;
            }
        }

        public static void PostWorkitemRequest(string desc, string urlKey, string workerId, string workitemId, Dictionary<String, Object> payload = null)
        {
            var argDict = new Dictionary<string, string>
                {
                    { "signature", "PrUpNw1dM3zRH6j3eviklCHE9Zbvk9NavGcJ_CibW19h50Yvr-ZZYZqn5Gi_SG1cPVQEIZf2wAJgBmq4dhNj7w7t9wUEz2pcGhn-6kIRO--QqWy121gksPE8B103RtMzuOsQDcErk4LriRQRO7-Xqks-RtpBUnpInnS_lkkajQs" },
                    { "workitemId", workitemId },
                    { "workerId", workerId },
                };
            if (payload != null)
            {
                argDict["payload"] = JsonConvert.SerializeObject(payload);
            }
            NetClient.PostData(urlKey, argDict, out var retStr);
        }

        public static void StartAndComplete(string workerId, string workitemId, Dictionary<String, Object> completePayload = null, Dictionary<String, Object> startPayload = null)
        {
            PostWorkitemRequest("Skip", GlobalContext.LocationDict["WorkitemStart"], workerId, workitemId, startPayload);
            //PostWorkitemRequest("Complete", GlobalContext.LocationDict["WorkitemComplete"], workerId, workitemId, completePayload);
        }

        public static void ReadLocationFromSteady()
        {
            GlobalContext.LocationDict.Clear();
            var fs = new FileStream(ParseURItoURL(GlobalContext.LocationConfigFilePath), FileMode.Open);
            var sr = new StreamReader(fs);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                var lineItem = line.Split('\t');
                if (lineItem.Length == 2)
                {
                    lineItem[1] = lineItem[1].Replace("\r", "").Replace("\n", "").Trim();
                    GlobalContext.LocationDict.Add(lineItem[0].Trim(), lineItem[1]);
                }
            }
            sr.Close();
            fs.Close();
            var outputStr = GlobalContext.LocationDict.Aggregate("", (current, kvp) => current + $"{kvp.Key} => {kvp.Value}\n");
        }

        /// <summary>
        /// 把一个相对URI转化为绝对路径
        /// </summary>
        /// <param name="uri">相对程序运行目录的相对路径</param>
        /// <returns>绝对路径</returns>
        public static string ParseURItoURL(string uri)
        {
            return AppDomain.CurrentDomain.BaseDirectory + uri;
        }
    }


    /// <summary>  
    /// 获取Post请求参数键值对的辅助类  
    /// </summary>  
    public class HttpListenerPostParaHelper
    {
        /// <summary>
        /// 构造一个键值对获取器
        /// </summary>
        /// <param name="request">HTTP请求上下文</param>
        public HttpListenerPostParaHelper(HttpListenerContext request)
        {
            this.request = request;
        }

        /// <summary>
        /// 对比两个字节流是否相等
        /// </summary>
        /// <param name="source">比较流1</param>
        /// <param name="comparison">比较流2</param>
        /// <returns>是否相等</returns>
        private static bool CompareBytes(IList<byte> source, IList<byte> comparison)
        {
            try
            {
                int count = source.Count;
                if (source.Count != comparison.Count)
                {
                    return false;
                }
                for (int i = 0; i < count; i++)
                {
                    if (source[i] != comparison[i])
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 将行数据读取为字节流
        /// </summary>
        /// <param name="SourceStream">要读取的流</param>
        /// <returns>字节流</returns>
        private static byte[] ReadLineAsBytes(Stream SourceStream)
        {
            var resultStream = new MemoryStream();
            while (true)
            {
                int data = SourceStream.ReadByte();
                resultStream.WriteByte((byte)data);
                if (data == 10)
                    break;
            }
            resultStream.Position = 0;
            byte[] dataBytes = new byte[resultStream.Length];
            resultStream.Read(dataBytes, 0, dataBytes.Length);
            return dataBytes;
        }

        /// <summary>  
        /// 获取Post过来的参数和数据  
        /// </summary>  
        /// <returns></returns>  
        public List<HttpListenerPostValue> GetHttpListenerPostValue()
        {
            try
            {
                List<HttpListenerPostValue> HttpListenerPostValueList = new List<HttpListenerPostValue>();
                if (request.Request.ContentType.Length > 20 && string.Compare(request.Request.ContentType.Substring(0, 20), "multipart/form-data;", true) == 0)
                {
                    string[] HttpListenerPostValue = request.Request.ContentType.Split(';').Skip(1).ToArray();
                    string boundary = string.Join(";", HttpListenerPostValue).Replace("boundary=", "").Trim();
                    byte[] ChunkBoundary = Encoding.UTF8.GetBytes("--" + boundary + "\r\n");
                    byte[] EndBoundary = Encoding.UTF8.GetBytes("--" + boundary + "--\r\n");
                    Stream SourceStream = request.Request.InputStream;
                    var resultStream = new MemoryStream();
                    bool CanMoveNext = true;
                    HttpListenerPostValue data = null;
                    while (CanMoveNext)
                    {
                        byte[] currentChunk = ReadLineAsBytes(SourceStream);
                        if (!Encoding.UTF8.GetString(currentChunk).Equals("\r\n"))
                            resultStream.Write(currentChunk, 0, currentChunk.Length);
                        if (CompareBytes(ChunkBoundary, currentChunk))
                        {
                            byte[] result = new byte[resultStream.Length - ChunkBoundary.Length];
                            resultStream.Position = 0;
                            resultStream.Read(result, 0, result.Length);
                            CanMoveNext = true;
                            if (result.Length > 0)
                                data.RawValueData = result;
                            data = new HttpListenerPostValue();
                            HttpListenerPostValueList.Add(data);
                            resultStream.Dispose();
                            resultStream = new MemoryStream();

                        }
                        else if (Encoding.UTF8.GetString(currentChunk).Contains("Content-Disposition"))
                        {
                            byte[] result = new byte[resultStream.Length - 2];
                            resultStream.Position = 0;
                            resultStream.Read(result, 0, result.Length);
                            CanMoveNext = true;
                            data.Key = Encoding.UTF8.GetString(result).Replace("Content-Disposition: form-data; name=\"", "").Replace("\"", "").Split(';')[0];
                            resultStream.Dispose();
                            resultStream = new MemoryStream();
                        }
                        else if (Encoding.UTF8.GetString(currentChunk).Contains("Content-Type"))
                        {
                            CanMoveNext = true;
                            data.Type = 1;
                            resultStream.Dispose();
                            resultStream = new MemoryStream();
                        }
                        else if (CompareBytes(EndBoundary, currentChunk))
                        {
                            byte[] result = new byte[resultStream.Length - EndBoundary.Length - 2];
                            resultStream.Position = 0;
                            resultStream.Read(result, 0, result.Length);
                            data.RawValueData = result;
                            resultStream.Dispose();
                            CanMoveNext = false;
                        }
                    }
                }
                else
                {
                    var ss = request.Request.InputStream;
                    var sr = new StreamReader(ss);
                    var argStr = sr.ReadToEnd();
                    sr.Close();
                    ss.Close();
                    var argItem = argStr.Split('&');
                    HttpListenerPostValueList.AddRange(argItem
                        .Select(argKVP => argKVP.Split('='))
                        .Select(argPair => new HttpListenerPostValue
                        {
                            Key = argPair[0],
                            DirectStr = argPair[1]
                        }));
                }
                return HttpListenerPostValueList;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 请求上下文对象
        /// </summary>
        private readonly HttpListenerContext request;
    }

    /// <summary>  
    /// HttpListenner监听Post请求参数值实体  
    /// </summary>  
    public class HttpListenerPostValue
    {
        public int Type;

        public string Key { get; set; }

        public byte[] RawValueData { get; set; }

        public string DirectStr { get; set; }

        public string ValueString => this.DirectStr ?? Encoding.UTF8.GetString(this.RawValueData).TrimEnd('\r', '\n');
    }
}
