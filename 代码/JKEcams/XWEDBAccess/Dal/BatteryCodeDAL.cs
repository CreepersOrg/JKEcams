using System;
using System.Data;
using System.Text;
using System.Data.SqlClient;
using XWEDBAccess.DBUtility;
 
namespace XWEDBAccess.DAL
{
    /// <summary>
    /// 数据访问类:BatteryCode
    /// </summary>
    public partial class BatteryCodeDAL
    {
        public BatteryCodeDAL()
        { }
        #region  Method

        /// <summary>
        /// 是否存在该记录
        /// </summary>
        public bool Exists(long BatteryCodeID)
        {
            StringBuilder strSql = new StringBuilder();
            strSql.Append("select count(1) from BatteryCode");
            strSql.Append(" where BatteryCodeID=" + BatteryCodeID + " ");
            return DbHelperSQL.Exists(strSql.ToString());
        }

        /// <summary>
        /// 增加一条数据
        /// </summary>
        public long Add(XWEDBAccess.Model.BatteryCodeModel model)
        {
            StringBuilder strSql = new StringBuilder();
            StringBuilder strSql1 = new StringBuilder();
            StringBuilder strSql2 = new StringBuilder();
            if (model.Code != null)
            {
                strSql1.Append("Code,");
                strSql2.Append("'" + model.Code + "',");
            }
            if (model.GoodsSiteID != null)
            {
                strSql1.Append("GoodsSiteID,");
                strSql2.Append("" + model.GoodsSiteID + ",");
            }
            if (model.Channel != null)
            {
                strSql1.Append("Channel,");
                strSql2.Append("'" + model.Channel + "',");
            }
            if (model.Pressure != null)
            {
                strSql1.Append("Pressure,");
                strSql2.Append("'" + model.Pressure + "',");
            }
            if (model.InnerRC != null)
            {
                strSql1.Append("InnerRC,");
                strSql2.Append("'" + model.InnerRC + "',");
            }
            if (model.Power != null)
            {
                strSql1.Append("Power,");
                strSql2.Append("'" + model.Power + "',");
            }
            if (model.Capcity != null)
            {
                strSql1.Append("Capcity,");
                strSql2.Append("'" + model.Capcity + "',");
            }
            if (model.TestResult != null)
            {
                strSql1.Append("TestResult,");
                strSql2.Append("'" + model.TestResult + "',");
            }
            if (model.Temperature != null)
            {
                strSql1.Append("Temperature,");
                strSql2.Append("'" + model.Temperature + "',");
            }
            if (model.Electricity != null)
            {
                strSql1.Append("Electricity,");
                strSql2.Append("'" + model.Electricity + "',");
            }
            if (model.TestTime != null)
            {
                strSql1.Append("TestTime,");
                strSql2.Append("'" + model.TestTime + "',");
            }
            if (model.Tag1 != null)
            {
                strSql1.Append("Tag1,");
                strSql2.Append("'" + model.Tag1 + "',");
            }
            if (model.Tag2 != null)
            {
                strSql1.Append("Tag2,");
                strSql2.Append("'" + model.Tag2 + "',");
            }
            if (model.Tag3 != null)
            {
                strSql1.Append("Tag3,");
                strSql2.Append("'" + model.Tag3 + "',");
            }
            if (model.Tag4 != null)
            {
                strSql1.Append("Tag4,");
                strSql2.Append("'" + model.Tag4 + "',");
            }
            if (model.Tag5 != null)
            {
                strSql1.Append("Tag5,");
                strSql2.Append("'" + model.Tag5 + "',");
            }
            strSql.Append("insert into BatteryCode(");
            strSql.Append(strSql1.ToString().Remove(strSql1.Length - 1));
            strSql.Append(")");
            strSql.Append(" values (");
            strSql.Append(strSql2.ToString().Remove(strSql2.Length - 1));
            strSql.Append(")");
            strSql.Append(";select @@IDENTITY");
            object obj = DbHelperSQL.GetSingle(strSql.ToString());
            if (obj == null)
            {
                return 0;
            }
            else
            {
                return Convert.ToInt64(obj);
            }
        }

        /// <summary>
        /// 更新一条数据
        /// </summary>
        public bool Update(XWEDBAccess.Model.BatteryCodeModel model)
        {
            StringBuilder strSql = new StringBuilder();
            strSql.Append("update BatteryCode set ");
            if (model.Code != null)
            {
                strSql.Append("Code='" + model.Code + "',");
            }
            else
            {
                strSql.Append("Code= null ,");
            }
            if (model.GoodsSiteID != null)
            {
                strSql.Append("GoodsSiteID=" + model.GoodsSiteID + ",");
            }
            if (model.Channel != null)
            {
                strSql.Append("Channel='" + model.Channel + "',");
            }
            else
            {
                strSql.Append("Channel= null ,");
            }
            if (model.Pressure != null)
            {
                strSql.Append("Pressure='" + model.Pressure + "',");
            }
            else
            {
                strSql.Append("Pressure= null ,");
            }
            if (model.InnerRC != null)
            {
                strSql.Append("InnerRC='" + model.InnerRC + "',");
            }
            else
            {
                strSql.Append("InnerRC= null ,");
            }
            if (model.Power != null)
            {
                strSql.Append("Power='" + model.Power + "',");
            }
            else
            {
                strSql.Append("Power= null ,");
            }
            if (model.Capcity != null)
            {
                strSql.Append("Capcity='" + model.Capcity + "',");
            }
            else
            {
                strSql.Append("Capcity= null ,");
            }
            if (model.TestResult != null)
            {
                strSql.Append("TestResult='" + model.TestResult + "',");
            }
            else
            {
                strSql.Append("TestResult= null ,");
            }
            if (model.Temperature != null)
            {
                strSql.Append("Temperature='" + model.Temperature + "',");
            }
            else
            {
                strSql.Append("Temperature= null ,");
            }
            if (model.Electricity != null)
            {
                strSql.Append("Electricity='" + model.Electricity + "',");
            }
            else
            {
                strSql.Append("Electricity= null ,");
            }
            if (model.TestTime != null)
            {
                strSql.Append("TestTime='" + model.TestTime + "',");
            }
            else
            {
                strSql.Append("TestTime= null ,");
            }
            if (model.Tag1 != null)
            {
                strSql.Append("Tag1='" + model.Tag1 + "',");
            }
            else
            {
                strSql.Append("Tag1= null ,");
            }
            if (model.Tag2 != null)
            {
                strSql.Append("Tag2='" + model.Tag2 + "',");
            }
            else
            {
                strSql.Append("Tag2= null ,");
            }
            if (model.Tag3 != null)
            {
                strSql.Append("Tag3='" + model.Tag3 + "',");
            }
            else
            {
                strSql.Append("Tag3= null ,");
            }
            if (model.Tag4 != null)
            {
                strSql.Append("Tag4='" + model.Tag4 + "',");
            }
            else
            {
                strSql.Append("Tag4= null ,");
            }
            if (model.Tag5 != null)
            {
                strSql.Append("Tag5='" + model.Tag5 + "',");
            }
            else
            {
                strSql.Append("Tag5= null ,");
            }
            int n = strSql.ToString().LastIndexOf(",");
            strSql.Remove(n, 1);
            strSql.Append(" where BatteryCodeID=" + model.BatteryCodeID + "");
            int rowsAffected = DbHelperSQL.ExecuteSql(strSql.ToString());
            if (rowsAffected > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// 删除一条数据
        /// </summary>
        public bool Delete(long BatteryCodeID)
        {
            StringBuilder strSql = new StringBuilder();
            strSql.Append("delete from BatteryCode ");
            strSql.Append(" where BatteryCodeID=" + BatteryCodeID + "");
            int rowsAffected = DbHelperSQL.ExecuteSql(strSql.ToString());
            if (rowsAffected > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }		/// <summary>
        /// 批量删除数据
        /// </summary>
        public bool DeleteList(string BatteryCodeIDlist)
        {
            StringBuilder strSql = new StringBuilder();
            strSql.Append("delete from BatteryCode ");
            strSql.Append(" where BatteryCodeID in (" + BatteryCodeIDlist + ")  ");
            int rows = DbHelperSQL.ExecuteSql(strSql.ToString());
            if (rows > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }


        /// <summary>
        /// 得到一个对象实体
        /// </summary>
        public XWEDBAccess.Model.BatteryCodeModel GetModel(long BatteryCodeID)
        {
            StringBuilder strSql = new StringBuilder();
            strSql.Append("select  top 1  ");
            strSql.Append(" BatteryCodeID,Code,GoodsSiteID,Channel,Pressure,InnerRC,Power,Capcity,TestResult,Temperature,Electricity,TestTime,Tag1,Tag2,Tag3,Tag4,Tag5 ");
            strSql.Append(" from BatteryCode ");
            strSql.Append(" where BatteryCodeID=" + BatteryCodeID + "");
           XWEDBAccess.Model.BatteryCodeModel model = new XWEDBAccess.Model.BatteryCodeModel();
            DataSet ds = DbHelperSQL.Query(strSql.ToString());
            if (ds.Tables[0].Rows.Count > 0)
            {
                return DataRowToModel(ds.Tables[0].Rows[0]);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// 得到一个对象实体
        /// </summary>
        public XWEDBAccess.Model.BatteryCodeModel DataRowToModel(DataRow row)
        {
           XWEDBAccess.Model.BatteryCodeModel model = new XWEDBAccess.Model.BatteryCodeModel();
            if (row != null)
            {
                if (row["BatteryCodeID"] != null && row["BatteryCodeID"].ToString() != "")
                {
                    model.BatteryCodeID = long.Parse(row["BatteryCodeID"].ToString());
                }
                if (row["Code"] != null)
                {
                    model.Code = row["Code"].ToString();
                }
                if (row["GoodsSiteID"] != null && row["GoodsSiteID"].ToString() != "")
                {
                    model.GoodsSiteID = long.Parse(row["GoodsSiteID"].ToString());
                }
                if (row["Channel"] != null)
                {
                    model.Channel = row["Channel"].ToString();
                }
                if (row["Pressure"] != null)
                {
                    model.Pressure = row["Pressure"].ToString();
                }
                if (row["InnerRC"] != null)
                {
                    model.InnerRC = row["InnerRC"].ToString();
                }
                if (row["Power"] != null)
                {
                    model.Power = row["Power"].ToString();
                }
                if (row["Capcity"] != null)
                {
                    model.Capcity = row["Capcity"].ToString();
                }
                if (row["TestResult"] != null)
                {
                    model.TestResult = row["TestResult"].ToString();
                }
                if (row["Temperature"] != null)
                {
                    model.Temperature = row["Temperature"].ToString();
                }
                if (row["Electricity"] != null)
                {
                    model.Electricity = row["Electricity"].ToString();
                }
                if (row["TestTime"] != null && row["TestTime"].ToString() != "")
                {
                    model.TestTime = DateTime.Parse(row["TestTime"].ToString());
                }
                if (row["Tag1"] != null)
                {
                    model.Tag1 = row["Tag1"].ToString();
                }
                if (row["Tag2"] != null)
                {
                    model.Tag2 = row["Tag2"].ToString();
                }
                if (row["Tag3"] != null)
                {
                    model.Tag3 = row["Tag3"].ToString();
                }
                if (row["Tag4"] != null)
                {
                    model.Tag4 = row["Tag4"].ToString();
                }
                if (row["Tag5"] != null)
                {
                    model.Tag5 = row["Tag5"].ToString();
                }
            }
            return model;
        }

        /// <summary>
        /// 获得数据列表
        /// </summary>
        public DataSet GetList(string strWhere)
        {
            StringBuilder strSql = new StringBuilder();
            strSql.Append("select BatteryCodeID,Code,GoodsSiteID,Channel,Pressure,InnerRC,Power,Capcity,TestResult,Temperature,Electricity,TestTime,Tag1,Tag2,Tag3,Tag4,Tag5 ");
            strSql.Append(" FROM BatteryCode ");
            if (strWhere.Trim() != "")
            {
                strSql.Append(" where " + strWhere);
            }
            return DbHelperSQL.Query(strSql.ToString());
        }

        /// <summary>
        /// 获得前几行数据
        /// </summary>
        public DataSet GetList(int Top, string strWhere, string filedOrder)
        {
            StringBuilder strSql = new StringBuilder();
            strSql.Append("select ");
            if (Top > 0)
            {
                strSql.Append(" top " + Top.ToString());
            }
            strSql.Append(" BatteryCodeID,Code,GoodsSiteID,Channel,Pressure,InnerRC,Power,Capcity,TestResult,Temperature,Electricity,TestTime,Tag1,Tag2,Tag3,Tag4,Tag5 ");
            strSql.Append(" FROM BatteryCode ");
            if (strWhere.Trim() != "")
            {
                strSql.Append(" where " + strWhere);
            }
            strSql.Append(" order by " + filedOrder);
            return DbHelperSQL.Query(strSql.ToString());
        }

        /// <summary>
        /// 获取记录总数
        /// </summary>
        public int GetRecordCount(string strWhere)
        {
            StringBuilder strSql = new StringBuilder();
            strSql.Append("select count(1) FROM BatteryCode ");
            if (strWhere.Trim() != "")
            {
                strSql.Append(" where " + strWhere);
            }
            object obj = DbHelperSQL.GetSingle(strSql.ToString());
            if (obj == null)
            {
                return 0;
            }
            else
            {
                return Convert.ToInt32(obj);
            }
        }
        /// <summary>
        /// 分页获取数据列表
        /// </summary>
        public DataSet GetListByPage(string strWhere, string orderby, int startIndex, int endIndex)
        {
            StringBuilder strSql = new StringBuilder();
            strSql.Append("SELECT * FROM ( ");
            strSql.Append(" SELECT ROW_NUMBER() OVER (");
            if (!string.IsNullOrEmpty(orderby.Trim()))
            {
                strSql.Append("order by T." + orderby);
            }
            else
            {
                strSql.Append("order by T.BatteryCodeID desc");
            }
            strSql.Append(")AS Row, T.*  from BatteryCode T ");
            if (!string.IsNullOrEmpty(strWhere.Trim()))
            {
                strSql.Append(" WHERE " + strWhere);
            }
            strSql.Append(" ) TT");
            strSql.AppendFormat(" WHERE TT.Row between {0} and {1}", startIndex, endIndex);
            return DbHelperSQL.Query(strSql.ToString());
        }

        /*
        */

        #endregion  Method
        #region  MethodEx
        public bool DeleteByGSID(int goodssiteID)
        {

            StringBuilder strSql = new StringBuilder();
            strSql.Append("delete from BatteryCode ");
            strSql.Append(" where GoodsSiteID=" + goodssiteID + "");
            int rowsAffected = DbHelperSQL.ExecuteSql(strSql.ToString());
            if (rowsAffected > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

		/// <summary>
        /// 得到一个对象实体
        /// </summary>
        public XWEDBAccess.Model.BatteryCodeModel GetModelByCode(string Code)
        {
            StringBuilder strSql = new StringBuilder();
            strSql.Append("select  top 1  ");
            strSql.Append(" BatteryCodeID,Code,GoodsSiteID,Channel,Pressure,InnerRC,Power,Capcity,TestResult,TestTime,Tag1,Tag2,Tag3,Tag4,Tag5 ");
            strSql.Append(" from BatteryCode ");
            strSql.Append(" where Code = '" + Code + "' ");
            XWEDBAccess.Model.BatteryCodeModel model = new XWEDBAccess.Model.BatteryCodeModel();
            DataSet ds = DbHelperSQL.Query(strSql.ToString());
            if (ds.Tables[0].Rows.Count > 0)
            {
                return DataRowToModel(ds.Tables[0].Rows[0]);
            }
            else
            {
                return null;
            }
        }
        #endregion  MethodEx
    }
}

