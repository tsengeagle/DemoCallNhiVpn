using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CSHISXLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DemoCallNhiVpn
{
    class Program
    {
        static void Main(string[] args)
        {
            runUpload();
        }

        static void runUpload()
        {
            // 呼叫csHisX
            CSHISXLib.Inhicshisx csHisX = new CSHISXLib.nhicshisx();
            // 取sam卡
            var basicData = csHisX.GetSAMCardInfoInCS();
            JObject data = JsonConvert.DeserializeObject<JObject>(basicData);
            JObject samArray = (JObject)data.Property("SAMCardInfoInCS").Value;
            JObject samItem = (JObject)samArray.Property("SAM").Value[0];
            // 取院所ID
            string hospId = (string)samItem.Property("HOSP").Value;
            // 取SAM卡號
            string samId = (string)samItem.Property("CARD_ID").Value;
            // 取亂數
            var randomX = csHisX.VPNGetRandomX();
            // 取簽章
            var signature = csHisX.VPNH_SignX(randomX, "3", "30");
            Console.WriteLine("hosp=" + hospId);
            Console.WriteLine("samId=" + samId);
            Console.WriteLine("randomX=" + randomX);
            Console.WriteLine("signature=" + signature);

            const string opType = "A2"; // VPN的作業類別

            var xml = "<XML>";

            httpClient.BaseAddress = new Uri("https://medvpndti.nhi.gov.tw/V1000/");
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            // 排除 WebException: 要求已經終止: 無法建立SSL/TLS的安全連線
            System.Net.ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls;

            // 從指定路徑找xml，在D槽新增test目錄，將自備的xml或專案內的test.xml複製過去
            string path = @"D:\test\";
            var list = Directory.GetFiles(path, "*.xml", SearchOption.TopDirectoryOnly);
            foreach (var item in list)
            {
                xml = File.ReadAllText(item);
                Console.WriteLine("xml content: " + xml);
                string encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(xml));
                Console.WriteLine("encoded: " + encoded);
                UploadPayload uploadPayload = new UploadPayload();
                uploadPayload.sSamid = samId;
                uploadPayload.sHospid = hospId;
                uploadPayload.sClientrandom = randomX;
                uploadPayload.sSignature = signature;
                uploadPayload.sType = opType;
                uploadPayload.sMrecs = 1.ToString(); // TODO: 依照MB1出現的次數
                uploadPayload.sPrecs = 1.ToString(); // TODO: 依照MB1出現的次數
                uploadPayload.sPatData = encoded;
                uploadPayload.sUploadDT = DateTime.Now.ToString("yyyyMMddHHmmss") + "000";
                string payload = JsonConvert.SerializeObject(uploadPayload);
                Console.WriteLine(payload);

                Console.WriteLine("上傳");

                HttpResponseMessage responseMessage =
                    httpClient.PostAsJsonAsync("VNHI_Upload", uploadPayload).Result;
                if (responseMessage.IsSuccessStatusCode)
                {
                    var uploadResult = responseMessage.Content.ReadAsAsync<UploadResult>().Result;
                    Console.WriteLine("result: rtnCode=" + uploadResult.rtnCode + ", opCode=" + uploadResult.opCode);
                    if (uploadResult.rtnCode == "0000")
                    {
                        Console.WriteLine("上傳成功");
                    }
                    else
                    {
                        Console.WriteLine("上傳失敗: " + uploadResult.rtnCode);
                    }
                    uploadResult.fileName = item;
                    uploadResults.Add(uploadResult); //保存上傳結果
                }
                else
                {
                    Console.WriteLine("upload error: " + responseMessage.StatusCode);
                }
            }

            Console.WriteLine("上傳完畢");

            Console.WriteLine("下載檢核結果");
            while (downloadResults.Count < uploadResults.Count)
            {
                foreach (var item in uploadResults)
                {
                    Console.WriteLine("嘗試下載檢核結果: " + item.fileName);
                    DownloadPayload downloadPayload = new DownloadPayload();
                    downloadPayload.sSamid = samId;
                    downloadPayload.sHospid = hospId;
                    downloadPayload.sClientrandom = randomX;
                    downloadPayload.sSignature = signature;
                    downloadPayload.sType = opType;
                    downloadPayload.sOpcode = item.opCode;
                    HttpResponseMessage responseMessage = httpClient.PostAsJsonAsync("VNHI_Download", downloadPayload).Result;
                    if (responseMessage.IsSuccessStatusCode)
                    {
                        var downloadResult = responseMessage.Content.ReadAsAsync<DownloadResult>().Result;
                        Console.WriteLine("download result: " + downloadResult.rtnCode);
                        if (downloadResult.rtnCode == "5002")
                        {
                            Console.WriteLine("5002 檢核結果還沒準備好");
                        }
                        else
                        {
                            Console.WriteLine("json: " + downloadResult.tx_result_json);
                            var decodedArray = Convert.FromBase64String(downloadResult.tx_result_json);
                            var decodedString = Encoding.UTF8.GetString(decodedArray);
                            Console.WriteLine("decoded: " + decodedString);
                            downloadResult.decoded = decodedString;
                            downloadResults.Add(downloadResult);
                        }
                    }
                    else
                    {
                        Console.WriteLine("download error: " + responseMessage.StatusCode);
                    }

                }
                Console.WriteLine("收檢核結果");

                Thread.Sleep(10000);//等10秒再試
            }

            Console.WriteLine("test done");
            Console.ReadLine();
        }
        static HttpClient httpClient = new HttpClient();
        static List<UploadResult> uploadResults = new List<UploadResult>();
        static List<DownloadResult> downloadResults = new List<DownloadResult>();
    }
}
