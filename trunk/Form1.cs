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
    public partial class Form1 : Form {
        public Form1() {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e) {

            var formAddWorkout = new FormAddWorkout();
            if(formAddWorkout.ShowDialog() == DialogResult.OK) {


            }

            return;

            //var filePath = @"D:\Docs\Sport\mySport2";
            //var dbc = new SQLiteConnection("Data Source=" + filePath + ";Version=3;");//, SQLite.SQLiteOpenFlags.ReadOnly);

            //dbc.Open();

            var dbc = Program.GetConnection();
            SQLiteCommand comm = new SQLiteCommand("select * from workout", dbc);
            //comm.CommandText = "select * from workout";
            //var lst = comm.ExecuteQuery();

            //DataSet ds = new DataSet();

            //{
            //    using(var da = new SQLiteDataAdapter("select Name from Inventory", dbc)) {
            //        da.Fill(ds);
            //        dataGridView1.DataSource = ds.Tables[0].DefaultView;
            //    }
            //}


            using(SQLiteDataReader read = comm.ExecuteReader()) {
                //using(SQLiteDataAdapter da = new SQLiteDataAdapter(comm.CommandText, dbc)) {
                //    da.Fill(ds);
                //    dataGridView1.DataSource = ds.Tables[0].DefaultView;
                //}


                //DataTable dt = new DataTable();
                //dt.Load(read);
                //dataGridView1.DataSource = dt;
                //dataGridView1.DataBind();


                
                while(read.Read()) {
                    dataGridView1.Rows.Add(new object[] {
                        0, //read.GetValue(read.GetOrdinal("id")),  // U can use column index
                        read.GetValue(read.GetOrdinal("inventory")),  // Or column name like this
                        getDateString(read.GetValue(read.GetOrdinal("datetime"))),
                        read.GetValue(read.GetOrdinal("title")),
                        read.GetValue(read.GetOrdinal("distance")),
                        getDurationString(read.GetValue(read.GetOrdinal("time"))),
                    });
                }
            }


        }

        internal static string getDurationString(object seconds) {
            try {
                if(seconds is string str)
                    return str;
                return getDurationString((int)seconds);
            } catch {
                return "n/a";
            }
        }

        internal static string getDurationString(int seconds) {
            TimeSpan time = TimeSpan.FromSeconds(seconds);

            //here backslash is must to tell that colon is
            //not the part of format, it just a character that we want in output
            string str = time.ToString(@"hh\:mm\:ss\:fff");

            return str;
        }

        internal static string getDateString(object dateTime) {
            try {
                if(dateTime is string str)
                    return str;
                return getDateString((long)dateTime);
            } catch {
                return "n/a";
            }
        }
        internal static string getDateString(long dateTime) {
            var dto2 = DateTimeOffset.FromUnixTimeSeconds(dateTime);
            return dto2.LocalDateTime.ToShortDateString();
        }

        private void buttonAddService_Click(object sender, EventArgs e) {
            var formAddWorkout = new FormAddService();
            if(formAddWorkout.ShowDialog() == DialogResult.OK) {


            }

        }

        private void Form1_Load(object sender, EventArgs e) {
            var dbc = Program.GetConnection();

            var query = "select title, datetime(datetime, 'unixepoch') as datetime, inventory.name as inventory, time(time, 'unixepoch') as time, distance, '****' from workout as w inner join inventory on w.inventory = inventory.id order by datetime desc";
            //var query = "select title from workout";

            DataSet ds = new DataSet();

            {
                using(var da = new SQLiteDataAdapter(query, dbc)) {
                    da.Fill(ds);
                    dataGridView2.DataSource = ds.Tables[0].DefaultView;
                }
            }

            //return;


            SQLiteCommand comm = new SQLiteCommand(query, dbc);

            using(SQLiteDataReader read = comm.ExecuteReader()) {

                while(read.Read()) {
                    //var title = read.GetString(read.GetOrdinal("title"));

                    dataGridView1.Rows.Add(new object[] {

                        0, //read.GetValue(read.GetOrdinal("id")),  // U can use column index
                        read.GetValue(read.GetOrdinal("inventory")),  // Or column name like this
                        getDateString(read.GetValue(read.GetOrdinal("datetime"))),
                        read.GetString(read.GetOrdinal("title")),
                        read.GetValue(read.GetOrdinal("distance")),
                        getDurationString(read.GetValue(read.GetOrdinal("time"))),
                    });
                }
            }
        }

    }
}
