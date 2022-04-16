using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Point = NetTopologySuite.Geometries.Point;

namespace Sport {
    public partial class FormAddWorkout : Form {
        public FormAddWorkout() {
            InitializeComponent();
        }

        private void FormAddWorkout_Load(object sender, EventArgs e) {
            var dbc = Program.GetConnection();

            //string sql = 
            //SQLiteCommand comm = new SQLiteCommand("select * from workout", dbc);
            //comm.CommandText = "select * from workout";
            //var lst = comm.ExecuteQuery();

            DataSet ds = new DataSet();

            //using(var da = new SQLiteDataAdapter("select Name from Inventory", dbc)) {
            //    da.Fill(ds);
            //    comboBoxInventory.DataSource = ds.Tables[0].DefaultView;
            //}

            var comm = new SQLiteCommand("select Name from ActivityType", dbc);
            using(SQLiteDataReader read = comm.ExecuteReader()) {
                while(read.Read()) {
                    comboBoxType.Items.Add((string)read["Name"]);
                }
            }

            comboBoxType.SelectedIndex = 0;




            //    using(var da = new SQLiteDataAdapter("select Name from Activity", dbc)) {
            //    da.Fill(ds);
            //    comboBoxInventory.DataSource = ds.Tables[0].Rows;
            //}

        }

        List<int> inventoryIndices = new List<int>();

        class InventoryInfo {
            public string name;
            public long id;

            public override string ToString() {
                return name;
            }
        }

        private void comboBoxType_SelectedIndexChanged(object sender, EventArgs e) {
            var dbc = Program.GetConnection();
            int activityIndex = comboBoxType.SelectedIndex + 1;

            comboBoxInventory.Items.Clear();
            inventoryIndices.Clear();

            var comm = new SQLiteCommand($"select Name, id from Inventory where ActivityType = {activityIndex}", dbc);
            using(SQLiteDataReader read = comm.ExecuteReader()) {
                while(read.Read()) {
                    var ii = new InventoryInfo() {
                        name = (string)read["Name"],
                        id = (long)read["id"]
                    };
                    comboBoxInventory.Items.Add(ii);
                }
            }

            comboBoxInventory.SelectedIndex = 0;

        }

        private void buttonAdd_Click(object sender, EventArgs e) {
            try {
                var dbc = Program.GetConnection();
                var inventory = comboBoxInventory.SelectedItem as InventoryInfo;
                var dto = new DateTimeOffset(dateTimePicker1.Value);
                var date = dto.ToUnixTimeSeconds();
                int timeSec = (int)(numericUpDownHours.Value * 3600 + numericUpDownMinutes.Value * 60);

                using(var trans = dbc.BeginTransaction()) {
                    var comm = new SQLiteCommand("INSERT INTO `workout`(" +
                        "`inventory`,`datetime`,`distance`,`time`,`description`,`title`,`routes`) " +
                        $"VALUES ({inventory.id}, {date},'{numericUpDownDistance.Value}',{timeSec},'{textBoxDesc.Text}','{textBoxTitle.Text}',NULL);", dbc);
                    var res = comm.ExecuteNonQuery();
                    if(res == 0)
                        MessageBox.Show("");


                    if (!string.IsNullOrEmpty(currentTrackPath)) {
                        try {
                            var data = File.ReadAllBytes(currentTrackPath);
                            {
                                //comm = new SQLiteCommand("select seq + 1 from sqlite_sequence where name = 'workout'; ", dbc);
                                //var workout_id = (int)comm.ExecuteScalar();
                                comm = new SQLiteCommand("select last_insert_rowid()", dbc);
                                var workout_id = (long)comm.ExecuteScalar();
                                var command = new SQLiteCommand("insert into Tracks (workout, type, data, data_hash) values (@workout, @type, @data, @data_hash)", dbc);

                                command.Parameters.Add("@workout", DbType.Int32).Value = workout_id;
                                command.Parameters.Add("@type", DbType.Int32).Value = 0;
                                command.Parameters.Add("@data_hash", DbType.Int64).Value = vim.Hash.Long(data);
                                command.Parameters.Add(new SQLiteParameter() {
                                    ParameterName = "@data",
                                    Value = data,
                                    DbType = DbType.Binary
                                });
                                res = command.ExecuteNonQuery();
                                if (res == 0)
                                    MessageBox.Show("");

                            }
                        } catch (Exception ex) {
                            MessageBox.Show(ex.Message);
                            throw;
                        }
                    }
                    trans.Commit();
                }
            } catch (Exception ex) {
                    MessageBox.Show(ex.Message);
                DialogResult = DialogResult.Retry;
            }
        }

        private void FormAddWorkout_FormClosing(object sender, FormClosingEventArgs e) {
            if(DialogResult == DialogResult.Retry)
                e.Cancel = true;
        }

        private double gps2m(double lat_a, double lng_a, double lat_b, double lng_b) {
            float pk = (float)(180 / 3.14169);

            var a1 = lat_a / pk;
            var a2 = lng_a / pk;
            var b1 = lat_b / pk;
            var b2 = lng_b / pk;

            var t1 = Math.Cos(a1) * Math.Cos(a2) * Math.Cos(b1) * Math.Cos(b2);
            var t2 = Math.Cos(a1) * Math.Sin(a2) * Math.Cos(b1) * Math.Sin(b2);
            var t3 = Math.Sin(a1) * Math.Sin(b1);
            double tt = Math.Acos(t1 + t2 + t3);

            return 6366000 * tt;
        }

        string currentTrackPath;
        void LoadGPX(string path) {
            try {
                var file = GpxFile.ReadFrom(XmlReader.Create(path), null);
                textBoxDesc.Text = file.Metadata.Description;
                if (DateTime.TryParse(file.Metadata.Name, out DateTime dt)) {
                    dateTimePicker1.Value = dt;
                }

                var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

                double fullDist = 0;
                GpxWaypoint prev_wp = null;

                TimeSpan timeSpan = new TimeSpan();
                TimeSpan timeSpanMove = new TimeSpan();

                foreach (var track in file.Tracks) {
                    foreach (var seg in track.Segments) {
                        foreach (var wp in seg.Waypoints) {
                            if (prev_wp != null) {
                                var distance = gps2m(wp.Latitude.Value, wp.Longitude.Value, prev_wp.Latitude.Value, prev_wp.Longitude.Value);
                                //var distance = point.Distance(prevPoint);
                                fullDist += distance;
                                if (wp.TimestampUtc.HasValue && prev_wp.TimestampUtc.HasValue) {
                                    timeSpan = timeSpan.Add(wp.TimestampUtc.Value - prev_wp.TimestampUtc.Value);
                                    if (distance > 1e-06)
                                        timeSpanMove = timeSpan.Add(wp.TimestampUtc.Value - prev_wp.TimestampUtc.Value);
                                }
                            }
                            prev_wp = wp;
                        }
                    }
                }

                numericUpDownHours.Value = timeSpan.Hours;
                numericUpDownMinutes.Value = timeSpan.Minutes;

                numericUpDownDistance.Value = (decimal)(0.001 * fullDist);
                currentTrackPath = path;
            } catch (Exception ex) {
                MessageBox.Show($"Load treck error:\n{ex.Message}");
            }
        }

        void updateServiceInfo() {
            try {
                var dbc = Program.GetConnection();
                var inventoryIndex = (comboBoxInventory.SelectedItem as InventoryInfo).id;
                var dto = new DateTimeOffset(dateTimePicker1.Value);
                var date = dto.ToUnixTimeSeconds();

                var comm = new SQLiteCommand($"select * from Service where inventory = {inventoryIndex} AND date < {date} ORDER BY date DESC", dbc);
                var lst = new List<string>();
                using(SQLiteDataReader read = comm.ExecuteReader()) {
                    while(read.Read()) {
                        var service_date = (long)read["date"];

                        var dto2 = DateTimeOffset.FromUnixTimeSeconds(service_date);
                        lst.Add((string)read["description"]);

                        if(lst.Count == 1) {
                            textBoxSericeInfoDate.Text = dto2.LocalDateTime.ToShortDateString();

                            var commDist = new SQLiteCommand($"select sum(distance) from workout where inventory = {inventoryIndex} AND datetime >= {service_date}", dbc);
                            var dist = commDist.ExecuteScalar();
                            textBoxSericeInfoDist.Text = (dist != null && !(dist is DBNull) ? (double)dist : 0).ToString();
                        }
                    }
                }


                if(lst.Count > 0) {
                    textBoxServiceInfo.Text = lst[0];
                } else {
                    textBoxSericeInfoDist.Text = textBoxSericeInfoDate.Text = textBoxServiceInfo.Text = "";
                }
            } catch (Exception){
            }
        }

        private void comboBoxInventory_SelectedIndexChanged(object sender, EventArgs e) {
            updateServiceInfo();
        }

        private void buttonLoadTrack_Click(object sender, EventArgs e) {
            var fileDlg = new OpenFileDialog() { CheckFileExists = true };
            if (fileDlg.ShowDialog() == DialogResult.OK) {

                //LoadGPX(@"D:\Docs\Downloads\11.01.2022 23_02.gpx");
                LoadGPX(fileDlg.FileName);
            }
        }
    }
}
