﻿using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Tooling.Connector;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;
using HttpClient = System.Net.Http.HttpClient;

namespace AshV.WebApiTester.XTB
{
    public partial class WebApiTesterControl : PluginControlBase, IGitHubPlugin, IHelpPlugin
    {
        private Settings mySettings;

        public string RepositoryName => "AshV.WebApiTester.XTB";

        public string UserName => "AshV";

        public string HelpUrl => "https://github.com/AshV/AshV.WebApiTester.XTB/wiki";

        private AppInsights ai;
        private const string aiEndpoint = "https://dc.services.visualstudio.com/v2/track";

        private const string aiKey = "175ccdd7-f61b-4793-8bb5-a88c512310e0";

        public WebApiTesterControl()
        {
            InitializeComponent();
            ai = new AppInsights(aiEndpoint, aiKey, Assembly.GetExecutingAssembly());
            ai.WriteEvent("Control Loaded");
        }

        private void MyPluginControl_Load(object sender, EventArgs e)
        {
            ApplyTheme(this);
            InitCustomStyle();
            RequestHeaders();

            if (!AuthTypeCheck()) return;

            // Loads or creates the settings for the plugin
            if (!SettingsManager.Instance.TryLoad(GetType(), out mySettings))
            {
                mySettings = new Settings();

                LogWarning("Settings not found => a new settings file has been created!");
            }
            else
            {
                LogInfo("Settings found and loaded");
            }
            PopulateFavourites();
        }

        private void PopulateFavourites()
        {
            cboFavourites.Items.Clear();
            cboFavourites.Items.AddRange(mySettings.Requests.ToArray());
        }

        private void tsbClose_Click(object sender, EventArgs e)
        {
            CloseTool();
        }

        private void ExecuteWebApiRequest()
        {
            if (!AuthTypeCheck()) return;


            WorkAsync(new WorkAsyncInfo
            {
                Message = "Executing WebAPI Request...",
                AsyncArgument = cmbMethod.SelectedItem.ToString(),
                Work = (worker, args) =>
                {
                    try
                    {
                        var csc = ConnectionDetail.GetCrmServiceClient();
                        switch (args.Argument.ToString())
                        {
                            case "GET":
                                if (txtRequestUri.Text.StartsWith("<fetch"))
                                {
                                    if (MessageBox.Show("Direct <fetchXml/> Execution is not supported yet. To see how to Execute <fetchXml/> using WebAPI, Please follow the link.", "Visit", MessageBoxButtons.OKCancel, MessageBoxIcon.Asterisk) == DialogResult.Yes)
                                    {
                                        Process.Start("https://www.ashishvishwakarma.com/Execute-fetchXml-WebAPI-Dynamics-365-Using-JavaScript-Example/");
                                    }

                                    //var result = Service.RetrieveMultiple(new FetchExpression(txtRequestUri.Text));
                                    //dgvResponseTable.DataSource = result?.Entities;
                                    args.Result = new CustomResponse();
                                }
                                else
                                {
                                    args.Result = RequestHelper(csc, HttpMethod.Get, txtRequestUri.Text);
                                }
                                break;

                            case "POST":
                                args.Result = RequestHelper(csc, HttpMethod.Post, txtRequestUri.Text, txtRequestBody.Text);
                                break;

                            case "PATCH":
                                args.Result = RequestHelper(csc, new HttpMethod("PATCH"), txtRequestUri.Text, txtRequestBody.Text);
                                break;

                            case "DELETE":
                                args.Result = RequestHelper(csc, HttpMethod.Delete, txtRequestUri.Text);
                                break;

                            case "PUT":
                                args.Result = RequestHelper(csc, HttpMethod.Put, txtRequestUri.Text, txtRequestBody.Text);
                                break;

                            default:
                                MessageBox.Show("Request is not proper!");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message + " | " + ex?.InnerException?.Message);
                    }
                },
                PostWorkCallBack = (args) =>
                {
                    if (args.Error != null)
                    {
                        MessageBox.Show(args.Error.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    ai.WriteEvent("API Called Successfully", 1);
                    var cr = args.Result as CustomResponse;
                    var result = cr.Response;
                    if (result != null)
                    {
                        txtRequestUri.Text = result.RequestMessage.RequestUri.ToString();
                        //Set response body tab as active
                        tabReqestResponse.SelectedIndex = 1;
                        tabResponseChild.SelectedIndex = 0;

                        if (!string.IsNullOrEmpty(cr.ResponseBody))
                            txtResponseBody.Text =
                            cr.ResponseBody.StartsWith("{") ?
                            JValue.Parse(cr.ResponseBody).ToString(Newtonsoft.Json.Formatting.Indented)
                            : cr.ResponseBody.StartsWith("<") ?
                            cr.ResponseBody :
                            cr.ResponseBody;

                        btnSend.BackColor = result.IsSuccessStatusCode ? Color.Green : Color.Red;
                        var resultBool = result.IsSuccessStatusCode ? "✔️ Success!" : "❌ Failed!";
                        txtMessage.ForeColor = result.IsSuccessStatusCode ? Color.Green : Color.Red;

                        //lblMain.ForeColor = result.IsSuccessStatusCode ? Color.Green : Color.Red;
                        //  lblMain.Text = $"\n{resultBool}\n🌐 {(int)result.StatusCode} {result.StatusCode}\n📚 {cr.Size / 1024} KB\n⌛ {cr.TimeSpent} ms";
                        txtMessage.AppendText(Environment.NewLine + resultBool);
                        txtMessage.AppendText(Environment.NewLine +$"🌐 { (int)result.StatusCode} { result.StatusCode}");
                        txtMessage.AppendText(Environment.NewLine + $"📚 {cr.Size / 1024} KB");
                        txtMessage.AppendText(Environment.NewLine + $"⌛ {cr.TimeSpent} ms");

                        //txtMessage.Text = $"\r\n{resultBool}\r\n🌐 {(int)result.StatusCode} {result.StatusCode}\r\n📚 {cr.Size / 1024} KB\r\n⌛ {cr.TimeSpent} ms";

                        if (cr.ResponseBody.StartsWith("{"))
                        {
                            var j = JsonConvert.DeserializeObject<GetMultpleResponse>(cr.ResponseBody);
                            if (!(j.value is null))
                            {
                                //lblMain.Text += $"\n\n🎬 {j.value.Count()} Records!";
                                txtMessage.AppendText( Environment.NewLine + $"\r\n🎬 {j.value.Count()} Records!");

                                dgvResponseTable.DataSource = ToDataTable(j.value);
                                tabResponseChild.SelectedIndex = 1;
                            }
                        }

                        dgvResponseHeaders.DataSource = cr.Headers;
                        dgvResponseHeaders.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                        dgvResponseHeaders.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                        dgvResponseHeaders.Columns[0].SortMode = DataGridViewColumnSortMode.Automatic;
                        dgvResponseHeaders.Columns[1].SortMode = DataGridViewColumnSortMode.Automatic;
                    }
                }
            });
        }

        /// <summary>
        /// This event occurs when the plugin is closed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MyPluginControl_OnCloseTool(object sender, EventArgs e)
        {
            // Before leaving, save the settings
            SettingsManager.Instance.Save(GetType(), mySettings);
        }

        /// <summary>
        /// This event occurs when the connection has been updated in XrmToolBox
        /// </summary>
        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            base.UpdateConnection(newService, detail, actionName, parameter);

            if (mySettings != null && detail != null)
            {
                mySettings.LastUsedOrganizationWebappUrl = detail.WebApplicationUrl;
                LogInfo("Connection has changed to: {0}", detail.WebApplicationUrl);
            }
        }

        internal CustomResponse RequestHelper(CrmServiceClient csc, HttpMethod method, string queryString, string body = null)
        {

            if (!csc.IsReady)
            {
                MessageBox.Show("Service initiation failed! Try in a moment or restart the tool.", null);
                return new CustomResponse();
            }
            var token = csc.CurrentAccessToken;
            var cr = new CustomResponse();
            cr.Headers = new List<KeyValuePair<string, string>>();
            cr.StartedAt = DateTime.Now;

            var client = new HttpClient();
            cr.Endpoint = $"https://{csc.CrmConnectOrgUriActual.Host}/api/data/v{csc.ConnectedOrgVersion}";
            cr.ApiVersion = csc.ConnectedOrgVersion.ToString();
            var msg = new HttpRequestMessage(method, cr.Endpoint + PrepareUri(queryString));
            msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("bearer", token);

            var customHeaders = GetSelectedHeaders();
            customHeaders.ForEach(kv =>
            {
                msg.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            });

            if (!string.IsNullOrEmpty(body))
            {
                msg.Content = new StringContent(
                    body,
                    UnicodeEncoding.UTF8,
                    "application/json");
            }

            var timer = new Stopwatch();
            timer.Start();
            var response = client.SendAsync(msg).Result;
            var responseBody = response.Content.ReadAsStringAsync().Result;
            timer.Stop();
            cr.TimeSpent = timer.ElapsedMilliseconds;
            cr.FinishedAt = DateTime.Now;

            cr.Response = response;
            cr.ResponseBody = responseBody;

            cr.ContentSize = responseBody.LongCount();
            cr.ResponseSize = (long)response.Content.Headers.ContentLength;
            cr.Size = cr.ResponseSize + cr.ContentSize;

            var list = "";
            var header = response.Headers;
            var h = header.GetEnumerator();
            do
            {
                list += h.Current.Key + " : " + JsonConvert.SerializeObject(h.Current.Value) + Environment.NewLine;
                cr.Headers.Add(new KeyValuePair<string, string>(h.Current.Key, JsonConvert.SerializeObject(h.Current.Value)));
            } while (h.MoveNext());
            list += "------------------" + Environment.NewLine;
            var header1 = response.Content.Headers;
            var h1 = header1.GetEnumerator();
            do
            {
                list += h1.Current.Key + " : " + JsonConvert.SerializeObject(h1.Current.Value) + Environment.NewLine;
                cr.Headers.Add(new KeyValuePair<string, string>(h.Current.Key, JsonConvert.SerializeObject(h.Current.Value)));
            } while (h1.MoveNext());
            cr.Headers.RemoveAll((a) => { return string.IsNullOrWhiteSpace(a.Key); });

            return cr;
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            timerSendButton.Start();
            GetReadyForNewResponse();
            btnSend.Enabled = false;
            ExecuteMethod(ExecuteWebApiRequest);
        }

        internal void ApplyTheme(UserControl parent)
        {
            foreach (Control c in parent.Controls)
                UpdateColorControls(c);

            void UpdateColorControls(Control myControl)
            {
                myControl.Font = new System.Drawing.Font("Verdana", 8);

                //if (!(myControl is Button) || !(myControl is ComboBox))
                //    myControl.BackColor = Color.White;

                if (myControl is TextBox)
                {
                    var txtBox = (TextBox)myControl;
                    txtBox.ForeColor = Color.Purple;
                    txtBox.BorderStyle = BorderStyle.FixedSingle;
                    txtBox.Font = new System.Drawing.Font("Verdana", 9);
                }

                foreach (Control subC in myControl.Controls)
                    UpdateColorControls(subC);
            }
        }

        internal void InitCustomStyle()
        {
            timerLogoRemove.Start();

            splitMain.SplitterDistance = 80;
            splitContainerRoot.SplitterDistance = 100;

            cmbMethod.SelectedIndex = 0;

            tabReqestResponse.Dock = DockStyle.Fill;
            tabRequestChild.Dock = DockStyle.Fill;
            txtRequestBody.Dock = DockStyle.Fill;
            tabResponseChild.Dock = DockStyle.Fill;
            txtResponseBody.Dock = DockStyle.Fill;

            txtResponseBody.ScrollBars = ScrollBars.Vertical;
            txtRequestUri.ScrollBars = ScrollBars.Vertical;
            txtRequestBody.ScrollBars = ScrollBars.Vertical;
        }

        /// <summary>
        /// Formats the provided XML so it's indented and humanly-readable.
        /// </summary>
        /// <param name="inputXml">The input XML to format.</param>
        /// <returns></returns>
        public static string FormatXml(string inputXml)
        {
            XmlDocument document = new XmlDocument();
            document.Load(new StringReader(inputXml));

            StringBuilder builder = new StringBuilder();
            using (XmlTextWriter writer = new XmlTextWriter(new StringWriter(builder)))
            {
                writer.Formatting = System.Xml.Formatting.Indented;
                document.Save(writer);
            }

            return builder.ToString();
        }

        private void timerSendButton_Tick(object sender, EventArgs e)
        {
            btnSend.Enabled = true;
            timerSendButton.Stop();
        }

        private void timerLogoRemove_Tick(object sender, EventArgs e)
        {
            splitLeftLower.Panel1.Controls.Remove(pictureBoxLogo);
        }

        internal void RequestHeaders()
        {

            var listHeaders = new List<Tuple<bool, string, string>>() {
                new Tuple<bool,string,string>(true,"Accept","application/json"),
                new Tuple<bool,string,string>(true,"OData-MaxVersion","4.0"),
                new Tuple<bool,string,string>(true,"OData-Version","4.0"),
                new Tuple<bool,string,string>(true,"If-None-Match","null"),
                new Tuple<bool,string,string>(true,"Content-Type","application/json"),
            };
            listHeaders.ForEach(row =>
            {
                dgvRequestHeaders.Rows.Add(row.Item1, row.Item2, row.Item3);
            });
        }

        internal List<KeyValuePair<string, string>> GetSelectedHeaders()
        {
            var list = new List<KeyValuePair<string, string>>();

            for (int i = 0; i < dgvRequestHeaders.RowCount; i++)
            {
                if (Convert.ToBoolean(dgvRequestHeaders.Rows[i].Cells[0].Value))
                {
                    var key = Convert.ToString(dgvRequestHeaders.Rows[i].Cells[1].Value);
                    var value = Convert.ToString(dgvRequestHeaders.Rows[i].Cells[2].Value);
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                        list.Add(new KeyValuePair<string, string>(key, value));
                }
            }

            return list;
        }

        internal DataTable ToDataTable(IEnumerable<dynamic> items)
        {
            var data = items.ToArray();
            if (data.Count() == 0) return null;

            var colList = new List<string>();
            var dt = new DataTable();
            foreach (var i in data)
            {
                DataRow row = dt.NewRow();
                foreach (var j in i)
                {
                    var k = Convert.ToString(j.Path);
                    var v = Convert.ToString(j.Value);

                    if (!colList.Contains(k))
                    {
                        dt.Columns.Add(k, typeof(string));
                        colList.Add(k);
                    }
                    row[k] = v;
                }
                dt.Rows.Add(row);
            }

            return dt;
        }

        internal void GetReadyForNewResponse()
        {
            btnSend.BackColor = Color.Purple;
            txtResponseBody.Text = "";
            dgvResponseTable.DataSource = null;
           // lblMain.Text = "";
            txtMessage.Text = string.Empty;
        }

        internal bool AuthTypeCheck()
        {
            if (ConnectionDetail?.AuthType != null && ConnectionDetail?.NewAuthType != null &&
                  ConnectionDetail.AuthType != Microsoft.Xrm.Sdk.Client.AuthenticationProviderType.OnlineFederation &&
                  (ConnectionDetail.NewAuthType != Microsoft.Xrm.Tooling.Connector.AuthenticationType.AD ||
                  ConnectionDetail.NewAuthType != Microsoft.Xrm.Tooling.Connector.AuthenticationType.OAuth ||
                  ConnectionDetail.NewAuthType != Microsoft.Xrm.Tooling.Connector.AuthenticationType.ClientSecret))
            {
                MessageBox.Show("Your connection type is not supported, Please connect using SDK Login Control to use this Tool.");
                return false;
            }
            return true;
        }

        internal static string PrepareUri(string requertUrl)
        {
            if (!requertUrl.StartsWith("/"))
                requertUrl = "/" + requertUrl;

            if (requertUrl.ToLowerInvariant().Contains("/api/data/v"))
            {
                requertUrl = requertUrl.Substring(requertUrl.IndexOf("/api/data/v"));
                requertUrl = requertUrl.Substring(requertUrl.IndexOf('v'));
                requertUrl = requertUrl.Substring(requertUrl.IndexOf('/'));
            }

            return requertUrl;
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            var addRequest = new AddRequest(mySettings);

            if (addRequest.ShowDialog() != DialogResult.OK) return;

            var request = new Request { Name = addRequest.SettingName, Description = addRequest.Description };
            request.Method = cmbMethod.SelectedItem.ToString();
            request.Uri = PrepareUri(txtRequestUri.Text);
            request.Body = txtRequestBody.Text;
            request.Headers.AddRange(GetHeaders(dgvRequestHeaders.Rows));
            if (mySettings.Requests.Any(mr => mr.Name == request.Name))
                mySettings.Requests[mySettings.Requests.IndexOf(request)] = request;
            else
                mySettings.Requests.Add(request);

            SettingsManager.Instance.Save(typeof(Settings), mySettings);

            PopulateFavourites();
        }
            
        private List<Header> GetHeaders(DataGridViewRowCollection rows)
        {
            List<Header> headers = new List<Header>();
            foreach (DataGridViewRow row in rows)
            {
                if (row.Cells[0]?.Value != null) headers.Add(new Header(row));
            }
            return headers;
        }

        private void cboFavourites_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cboFavourites.SelectedIndex == -1) return;
            Request request = cboFavourites.SelectedItem as Request;
            txtRequestBody.Text = request.Body;
            txtRequestUri.Text = request.Uri;
            cmbMethod.SelectedIndex = cmbMethod.Items.IndexOf(request.Method);
            dgvRequestHeaders.Rows.Clear();
            request.Headers.ForEach(header => dgvRequestHeaders.Rows.Add(header.Enable, header.Name, header.Value));
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            if (cboFavourites.SelectedIndex == -1 ) return;
            Request request = (Request)cboFavourites.SelectedItem;
            if (MessageBox.Show($"Do you want to remove {request.Name} from your favourite list?","Remove Favourite?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                mySettings.Requests.Remove(request);
                SettingsManager.Instance.Save(typeof(Settings), mySettings);

                PopulateFavourites();
                cboFavourites.SelectedIndex = -1;
                cboFavourites.Text = string.Empty;
            }
        }
    }
}