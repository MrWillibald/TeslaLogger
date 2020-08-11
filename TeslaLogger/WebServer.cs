﻿using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using System.Threading;
using System.Web.Script.Serialization;

namespace TeslaLogger
{
    public class WebServer
    {
        private HttpListener listener = null;

        public WebServer()
        {
            if (!HttpListener.IsSupported)
            {
                Logfile.Log("HttpListener is not Supported!!!");
                return;
            }
            
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://*:5000/");
                listener.Start();
            }
            catch (HttpListenerException hlex)
            {
                listener = null;
                if (((UInt32)hlex.HResult) == 0x80004005)
                {
                    Logfile.Log("HTTPListener access denied. Check https://stackoverflow.com/questions/4019466/httplistener-access-denied");
                }
                else
                {
                    Logfile.Log(hlex.ToString());
                }
            }
            catch (Exception ex)
            {
                listener = null;
                Logfile.Log(ex.ToString());
            }

            try
            {
                if (listener == null)
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add("http://localhost:5000/");
                    listener.Start();

                    Logfile.Log("HTTPListener only bound to Localhost!");
                }
            }
            catch (HttpListenerException hlex)
            {
                listener = null;
                if (((UInt32)hlex.HResult) == 0x80004005)
                {
                    Logfile.Log("HTTPListener access denied. Check https://stackoverflow.com/questions/4019466/httplistener-access-denied");
                }
                else
                {
                    Logfile.Log(hlex.ToString());
                }
            }
            catch (Exception ex)
            {
                listener = null;
                Logfile.Log(ex.ToString());
            }

            while (true)
            {
                try
                {
                    ThreadPool.QueueUserWorkItem(OnContext, listener.GetContext());
                }
                catch (Exception ex)
                {
                    Logfile.Log(ex.ToString());
                }
            }
        }

        private void OnContext(object o)
        {
            try
            {
                HttpListenerContext context = o as HttpListenerContext;

                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                switch (request.Url.LocalPath)
                {
                    case @"/getchargingstate":
                        Getchargingstate(request, response);
                        break;
                    case @"/setcost":
                        Setcost(request, response);
                        break;
                    case @"/debug/TeslaAPI/vehicles":
                    case @"/debug/TeslaAPI/charge_state":
                    case @"/debug/TeslaAPI/climate_state":
                    case @"/debug/TeslaAPI/drive_state":
                    case @"/debug/TeslaAPI/vehicle_config":
                    case @"/debug/TeslaAPI/vehicle_state":
                    case @"/debug/TeslaAPI/command/auto_conditioning_stop":
                    case @"/debug/TeslaAPI/command/charge_port_door_open":
                    case @"/debug/TeslaAPI/command/set_charge_limit":
                        Debug_TeslaAPI(request.Url.LocalPath, request, response);
                        break;
                    case @"/debug/TeslaLogger/states":
                        Debug_TeslaLoggerStates(request, response);
                        break;
                    default:
                        response.StatusCode = (int)HttpStatusCode.NotFound;
                        WriteString(response, @"URL Not Found!");
                        break;
                }

            }
            catch (Exception ex)
            {
                Logfile.Log(ex.ToString());
            }
        }

        private void Debug_TeslaLoggerStates(HttpListenerRequest request, HttpListenerResponse response)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            values.Add("Program._currentState", Program.GetCurrentState().ToString());
            values.Add("WebHelper._lastShift_State", Program.GetWebHelper().GetLastShiftState());
            values.Add("Program.highFreequencyLogging", Program.GetHighFreequencyLogging().ToString());
            values.Add("Program.highFrequencyLoggingTicks", Program.GetHighFrequencyLoggingTicks().ToString());
            values.Add("Program.highFrequencyLoggingTicksLimit", Program.GetHighFrequencyLoggingTicksLimit().ToString());
            values.Add("Program.highFrequencyLoggingUntil", Program.GetHighFrequencyLoggingUntil().ToString());
            values.Add("Program.highFrequencyLoggingMode", Program.GetHighFrequencyLoggingMode().ToString());
            values.Add("TLMemCacheKey.GetOutsideTempAsync",
                MemoryCache.Default.Get(Program.TLMemCacheKey.GetOutsideTempAsync.ToString()) != null
                ? ((double)MemoryCache.Default.Get(Program.TLMemCacheKey.GetOutsideTempAsync.ToString())).ToString()
                : "null");
            values.Add("Program.lastCarUsed", Program.GetLastCarUsed().ToString());
            values.Add("Program.lastOdometerChanged", Program.GetLastOdometerChanged().ToString());
            values.Add("Program.lastTryTokenRefresh", Program.GetLastTryTokenRefresh().ToString());
            values.Add("Program.lastSetChargeLimitAddressName",
                Program.GetLastSetChargeLimitAddressName().Equals(string.Empty)
                ? "&lt;&gt;"
                : Program.GetLastSetChargeLimitAddressName());
            values.Add("Program.goSleepWithWakeup", Program.GetGoSleepWithWakeup().ToString());
            values.Add("Program.odometerLastTrip", Program.GetOdometerLastTrip().ToString());
            values.Add("WebHelper.lastIsDriveTimestamp", Program.GetWebHelper().lastIsDriveTimestamp.ToString());
            values.Add("WebHelper.lastUpdateEfficiency", Program.GetWebHelper().lastUpdateEfficiency.ToString());
            IEnumerable<string> trs = values.Select(a => string.Format("<tr><td>{0}</td><td>{1}</td></tr>", a.Key, a.Value));
            WriteString(response, "<html><head></head><body><table>" + string.Concat(trs) + "</table></body></html>");
        }

        private void Debug_TeslaAPI(string path, HttpListenerRequest request, HttpListenerResponse response)
        {
            int position = path.LastIndexOf('/');
            if (position > -1)
            {
                path = path.Substring(position + 1);
                if (path.Length > 0 && WebHelper.TeslaAPI_Commands.TryGetValue(path, out string TeslaAPIJSON))
                {
                    response.AddHeader("Content-Type", "application/json");
                    WriteString(response, TeslaAPIJSON);
                }
            }
        }

        private void Setcost(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                Logfile.Log("SetCost");

                string json;

                if (request.QueryString["JSON"] != null)
                {
                    json = request.QueryString["JSON"];
                }
                else
                {
                    using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    {
                        json = reader.ReadToEnd();
                    }
                }

                Logfile.Log("JSON: " + json);

                dynamic j = new JavaScriptSerializer().DeserializeObject(json);

                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    MySqlCommand cmd = new MySqlCommand("update chargingstate set cost_total = @cost_total, cost_currency=@cost_currency, cost_per_kwh=@cost_per_kwh, cost_per_session=@cost_per_session, cost_per_minute=@cost_per_minute, cost_idle_fee_total=@cost_idle_fee_total, cost_kwh_meter_invoice=@cost_kwh_meter_invoice  where id= @id", con);

                    if (DBNullIfEmptyOrZero(j["cost_total"]) is DBNull && IsZero(j["cost_per_session"]))
                    {
                        cmd.Parameters.AddWithValue("@cost_total", 0);
                    }
                    else
                    {
                        cmd.Parameters.AddWithValue("@cost_total", DBNullIfEmptyOrZero(j["cost_total"]));
                    }

                    cmd.Parameters.AddWithValue("@cost_currency", DBNullIfEmpty(j["cost_currency"]));
                    cmd.Parameters.AddWithValue("@cost_per_kwh", DBNullIfEmpty(j["cost_per_kwh"]));
                    cmd.Parameters.AddWithValue("@cost_per_session", DBNullIfEmpty(j["cost_per_session"]));
                    cmd.Parameters.AddWithValue("@cost_per_minute", DBNullIfEmpty(j["cost_per_minute"]));
                    cmd.Parameters.AddWithValue("@cost_idle_fee_total", DBNullIfEmpty(j["cost_idle_fee_total"]));
                    cmd.Parameters.AddWithValue("@cost_kwh_meter_invoice", DBNullIfEmpty(j["cost_kwh_meter_invoice"]));

                    cmd.Parameters.AddWithValue("@id", j["id"]);
                    int done = cmd.ExecuteNonQuery();

                    Logfile.Log("SetCost OK: " + done);
                    WriteString(response, "OK");
                }
            }
            catch (Exception ex)
            {
                Logfile.Log(ex.ToString());
                WriteString(response, "ERROR");
            }
        }

        private object DBNullIfEmptyOrZero(string val)
        {
            return val == null || val == "" || val == "0" || val == "0.00" ? DBNull.Value : (object)val;
        }

        private object DBNullIfEmpty(string val)
        {
            return val == null || val == "" ? DBNull.Value : (object)val;
        }

        private bool IsZero(string val)
        {
            if (val == null || val == "")
            {
                return false;
            }

            if (double.TryParse(val, out double v))
            {
                if (v == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void Getchargingstate(HttpListenerRequest request, HttpListenerResponse response)
        {
            string id = request.QueryString["id"];
            string respone = "";

            try
            {
                Logfile.Log("HTTP getchargingstate");                
                DataTable dt = new DataTable();
                MySqlDataAdapter da = new MySqlDataAdapter("SELECT chargingstate.*, lat, lng, address, charging.charge_energy_added as kWh FROM chargingstate join pos on chargingstate.pos = pos.id join charging on chargingstate.EndChargingID = charging.id where chargingstate.id = @id", DBHelper.DBConnectionstring);
                da.SelectCommand.Parameters.AddWithValue("@id", id);
                da.Fill(dt);

                respone = dt.Rows.Count > 0 ? Tools.DataTableToJSONWithJavaScriptSerializer(dt) : "not found!";
            }
            catch (Exception ex)
            {
                Logfile.Log(ex.ToString());
            }

            WriteString(response, respone);
        }

        private static void WriteString(HttpListenerResponse response, string responseString)
        {
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            // Get a response stream and write the response to it.
            response.ContentLength64 = buffer.Length;
            Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
            // You must close the output stream.
            output.Close();
        }
    }
}
