using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sport {
    public partial class FormAddService : Form {
        public FormAddService() {
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

        private void comboBoxType_SelectedIndexChanged(object sender, EventArgs e) {
            var dbc = Program.GetConnection();
            int activityIndex = comboBoxType.SelectedIndex + 1;

            comboBoxInventory.Items.Clear();
            var comm = new SQLiteCommand($"select Name from Inventory where ActivityType = {activityIndex}", dbc);
            using(SQLiteDataReader read = comm.ExecuteReader()) {
                while(read.Read()) {
                    comboBoxInventory.Items.Add((string)read["Name"]);
                }
            }

            comboBoxInventory.SelectedIndex = 0;


            comboBoxServiceType.Items.Clear();
            comm = new SQLiteCommand($"select Name from ServiceType where ActivityType = {activityIndex}", dbc);
            using(SQLiteDataReader read = comm.ExecuteReader()) {
                while(read.Read()) {
                    comboBoxServiceType.Items.Add((string)read["Name"]);
                }
            }
            if(comboBoxServiceType.Items.Count > 0)
                comboBoxServiceType.SelectedIndex = 0;
        }

        private void buttonAdd_Click(object sender, EventArgs e) {
            try {
                var dbc = Program.GetConnection();
                var inventory = comboBoxInventory.SelectedIndex + 1;
                var serviceType = comboBoxServiceType.SelectedIndex + 1;
                var dto = new DateTimeOffset(dateTimePicker1.Value);
                var date = dto.ToUnixTimeSeconds();

                using(var trans = dbc.BeginTransaction()) {
                    var comm = new SQLiteCommand("INSERT INTO `Service`(`servicetype`,`inventory`,`description`,`date`) " +
                        $"VALUES ({serviceType}, {inventory},'{textBoxDesc.Text}',{date});", dbc);
                    var res = comm.ExecuteNonQuery();
                    if(res == 0)
                        MessageBox.Show("");
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

    }
}
