using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace vib {
    public partial struct ResultInfo {
        public bool IsOk => true;
    }
    public static partial class Utils {
        public static double LocalClockOffset {
            get { return localClockOffset; }
            set {
                localClockOffset = value;
            }
        }

        public static ResultInfo TryIO(Action action, string logStr, bool rethrow) {
            throw new NotImplementedException("TryIO not implemented for ReviztoClient and ReviztoSettings");
        }

        static double localClockOffset = 0.0;

        public static ResultInfo parseException(Exception e, string text) {
            return new ResultInfo();
        }
    }
}

namespace Sport {
    static class Program {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        static SQLiteConnection dbc;
        static internal SQLiteConnection GetConnection() {
            if(dbc == null) {
                var filePath = @"D:\Docs\Sport\mySport2";
                dbc = new SQLiteConnection("Data Source=" + filePath + ";Version=3;");//, SQLite.SQLiteOpenFlags.ReadOnly);

                dbc.Open();
            }

            return dbc;
        }

        internal static void Open() {
        }
    }
}
