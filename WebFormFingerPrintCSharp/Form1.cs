using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WebSocketSharp;
using System.Diagnostics;
using System.Threading;
using System.IO;
using MySql.Data.MySqlClient;

namespace WebFormFingerPrintCSharp
{
    public partial class Form1 : Form, DPFP.Capture.EventHandler
    {
        private WebSocket client;
        const string host = "ws://localhost:90";
        Bitmap img = null;
        DPFP.Capture.SampleConversion sp = new DPFP.Capture.SampleConversion();
        
        DPFP.Capture.Capture cp = new DPFP.Capture.Capture();
        DPFP.Sample sample = new DPFP.Sample();

        long inserted_id;

        string type;

        byte[] template = null;

        private DPFP.Processing.Enrollment Enroller = new DPFP.Processing.Enrollment();
        private DPFP.Verification.Verification Verificator = new DPFP.Verification.Verification();

        public Form1()
        {
            InitializeComponent();
            cp.EventHandler = this;

            websocket();


        }

        private void runQuery(byte[] ftemplate)
        {
            string connString = "datasource=127.0.0.1;port=3306;username=root;password=;database=studentcouncilvotingdb";
            string query = "INSERT INTO fTemplate('ftemplate') VALUES (@blobdata)";
            MySqlConnection dbConn = new MySqlConnection(connString);
            MySqlCommand dbCmd = new MySqlCommand(query, dbConn);

            try
            {
                dbConn.Open();
                using (var cmd = new MySqlCommand("INSERT INTO fTemplate SET fTemplate = @image",
                                  dbConn))
                {
                    cmd.Parameters.Add("@image", MySqlDbType.Blob).Value = ftemplate;
                    cmd.ExecuteNonQuery();
                    inserted_id = cmd.LastInsertedId;
                    Enroller = new DPFP.Processing.Enrollment();
                    template = null;
                    MessageBox.Show("Fingerprint scan complete.");
                    client.Send("enroll-" + inserted_id);
                }
                dbConn.Close();
            }
            catch (Exception e)
            {

                MessageBox.Show(e.ToString());
            }
        }

        private void websocket()
        {
            client = new WebSocket(host);

            client.OnOpen += (ss, ee) =>
                updateLabel("connected");


            client.OnError += (ss, ee) =>
               updateLabel("Error: " + ee.Message);


            client.OnMessage += (ss, ee) =>
                triggerFunction(ee.Data);


            client.OnClose += (ss, ee) =>
               updateLabel("Disonnected");
        }


        private void triggerFunction(string temp)
        {
            if (fromWebsocket.InvokeRequired)
            {
                fromWebsocket.Invoke((MethodInvoker)delegate()
                {
                    triggerFunction(temp);
                });
            }
            else
            {
                if (temp == "close")
                {
                    cp.StopCapture();
                    hideForm();
                }
                else if (temp.Contains("web-verify") || temp == "enroll")
                {
                    cp.StartCapture();
                    pictureBox1.Image = null;
                    showForm();
                    type = temp;
                }

                if (temp.Contains("web-verify"))
                {
                    updateLabel("Scan to verify user");
                    type = temp;
                }
                else if (temp.Contains("desktop-verify"))
                {
                    //do nothing
                }
                else
                {
                    updateLabel(temp);
                }

            }
        }

        public void showForm()
        {
            if (fromWebsocket.InvokeRequired)
            {
                fromWebsocket.Invoke((MethodInvoker)delegate()
                {
                    showForm();
                });
            }
            else
            {
                this.WindowState = FormWindowState.Normal;
                this.ShowInTaskbar = true;
            }


        }
        public void hideForm()
        {
            if (fromWebsocket.InvokeRequired)
            {
                fromWebsocket.Invoke((MethodInvoker)delegate()
                {
                    hideForm();
                });
            }
            else
            {
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
            }

        }


        public void finger()
        {
            #region Form Event Handlers
        }

        public void OnComplete(object Capture, string ReaderSerialNumber, DPFP.Sample Sample)
        {
            sp.ConvertToPicture(Sample, ref img);
            pictureBox1.Image = img;

            if (type == "enroll")
            {
                enroll(Sample);
            }
            else if (type.Contains("web-verify"))
            {
                verify(Sample);
            }

        }

        private void verify(DPFP.Sample Sample)
        {
            DPFP.FeatureSet features = ExtractFeatures(Sample, DPFP.Processing.DataPurpose.Verification);

            byte[] fpbyte = getFTemplate();
            if (fpbyte != null)
            {
                Stream stream = new MemoryStream(fpbyte);
                DPFP.Template tmpObj = new DPFP.Template(stream);


                DPFP.Verification.Verification.Result result = new DPFP.Verification.Verification.Result();
                // Compare the feature set with our template     
                Verificator.Verify(features, tmpObj, ref result);
                if (result.Verified)
                {
                    MessageBox.Show("The fingerprint was VERIFIED.");
                    client.Send("desktop-verify-verified");
                }
                else
                {
                    MessageBox.Show("The fingerprint was NOT VERIFIED.");
                    client.Send("desktop-verify-notverified");
                }

            }


            hideForm();
        }

        private byte[] getFTemplate()
        {
            string connString = "datasource=127.0.0.1;port=3306;username=root;password=;database=studentcouncilvotingdb";
            MySqlDataReader myData;
            MySqlConnection dbConn = new MySqlConnection(connString);

            try
            {
                byte[] template = null;
                dbConn.Open();
                using (var cmd = new MySqlCommand("SELECT ftemplate.fTemplate FROM user INNER JOIN ftemplate ON user.fTemplateId = ftemplate.id WHERE user.id = " + Int32.Parse(type.Replace("web-verify-", "")),
                                  dbConn))
                {
                    myData = cmd.ExecuteReader();

                    while (myData.Read())
                    {
                        template = (byte[])myData[0];
                    }
                }

                dbConn.Close();
                return template;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
                return null;
            }
        }

        public void enroll(DPFP.Sample Sample)
        {
            DPFP.FeatureSet features = ExtractFeatures(Sample, DPFP.Processing.DataPurpose.Enrollment);
            if (features != null)
            {
                Enroller.AddFeatures(features);     // Add feature set to template.
                if (Enroller.TemplateStatus == DPFP.Processing.Enrollment.Status.Ready)
                {
                    Enroller.Template.Serialize(ref template);
                    runQuery(template);
                    hideForm();
                    
                }
                else
                {
                    updateLabel("Lift Finger to Scan again");
                }
            }
        }

        public void updateLabel(string text)
        {
            if (fromWebsocket.InvokeRequired)
            {
                fromWebsocket.Invoke((MethodInvoker)delegate()
                {
                    updateLabel(text);
                });
            }
            else
            {
                fromWebsocket.Text = text;
            }

        }

        private DPFP.FeatureSet ExtractFeatures(DPFP.Sample Sample, DPFP.Processing.DataPurpose dataPurpose)
        {
            DPFP.FeatureSet features = new DPFP.FeatureSet();
            DPFP.Processing.FeatureExtraction Extractor = new DPFP.Processing.FeatureExtraction();
            DPFP.Capture.CaptureFeedback feedback = DPFP.Capture.CaptureFeedback.None;

            Extractor.CreateFeatureSet(Sample, dataPurpose, ref feedback, ref features);            // TODO: return features as a result?
            if (feedback == DPFP.Capture.CaptureFeedback.Good)
                return features;
            else
                return null;
        }

        public void OnFingerGone(object Capture, string ReaderSerialNumber)
        {
        }

        public void OnFingerTouch(object Capture, string ReaderSerialNumber)
        {
            updateLabel("");
            img = null;
        }

        public void OnReaderConnect(object Capture, string ReaderSerialNumber)
        {

        }

        public void OnReaderDisconnect(object Capture, string ReaderSerialNumber)
        {

        }

        public void OnSampleQuality(object Capture, string ReaderSerialNumber, DPFP.Capture.CaptureFeedback feedBack)
        {
            if (feedBack == DPFP.Capture.CaptureFeedback.Good)
            {
                fromWebsocket.Text = "Good Feedback Fingerprint";
            }
            else
            {
                fromWebsocket.Text = "Bad Feedback Fingerprint";
            }

        }

            #endregion
        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            client.Close();
        }


        private void Form1_Load(object sender, EventArgs e)
        {
            client.Connect();
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
        }
    }
}
