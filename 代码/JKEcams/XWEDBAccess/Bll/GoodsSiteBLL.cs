using System;
using System.Data;
using System.Collections.Generic;
using SysCfg;

namespace XWEDBAccess.BLL
{
    /// <summary>
    /// GoodsSite
    /// </summary>
    public partial class GoodsSiteBLL
    {
        private readonly XWEDBAccess.DAL.GoodsSiteDAL dal = new XWEDBAccess.DAL.GoodsSiteDAL();
        public GoodsSiteBLL()
        { }
        #region  BasicMethod
        /// <summary>
        /// 是否存在该记录
        /// </summary>
        public bool Exists(long GoodsSiteID)
        {
            return dal.Exists(GoodsSiteID);
        }

        /// <summary>
        /// 增加一条数据
        /// </summary>
        public long Add(XWEDBAccess.Model.GoodsSiteModel model)
        {
            return dal.Add(model);
        }

        /// <summary>
        /// 更新一条数据
        /// </summary>
        public bool Update(XWEDBAccess.Model.GoodsSiteModel model)
        {
            return dal.Update(model);
        }

        /// <summary>
        /// 删除一条数据
        /// </summary>
        public bool Delete(long GoodsSiteID)
        {

            return dal.Delete(GoodsSiteID);
        }
        /// <summary>
        /// 删除一条数据
        /// </summary>
        public bool DeleteList(string GoodsSiteIDlist)
        {
            return dal.DeleteList(GoodsSiteIDlist);
        }

        /// <summary>
        /// 得到一个对象实体
        /// </summary>
        public XWEDBAccess.Model.GoodsSiteModel GetModel(long GoodsSiteID)
        {

            return dal.GetModel(GoodsSiteID);
        }

 

        /// <summary>
        /// 获得数据列表
        /// </summary>
        public DataSet GetList(string strWhere)
        {
            return dal.GetList(strWhere);
        }
        /// <summary>
        /// 获得前几行数据
        /// </summary>
        public DataSet GetList(int Top, string strWhere, string filedOrder)
        {
            return dal.GetList(Top, strWhere, filedOrder);
        }
        /// <summary>
        /// 获得数据列表
        /// </summary>
        public List<XWEDBAccess.Model.GoodsSiteModel> GetModelList(string strWhere)
        {
            DataSet ds = dal.GetList(strWhere);
            return DataTableToList(ds.Tables[0]);
        }
        /// <summary>
        /// 获得数据列表
        /// </summary>
        public List<XWEDBAccess.Model.GoodsSiteModel> DataTableToList(DataTable dt)
        {
            List<XWEDBAccess.Model.GoodsSiteModel> modelList = new List<XWEDBAccess.Model.GoodsSiteModel>();
            int rowsCount = dt.Rows.Count;
            if (rowsCount > 0)
            {
                XWEDBAccess.Model.GoodsSiteModel model;
                for (int n = 0; n < rowsCount; n++)
                {
                    model = dal.DataRowToModel(dt.Rows[n]);
                    if (model != null)
                    {
                        modelList.Add(model);
                    }
                }
            }
            return modelList;
        }

        /// <summary>
        /// 获得数据列表
        /// </summary>
        public DataSet GetAllList()
        {
            return GetList("");
        }

        /// <summary>
        /// 分页获取数据列表
        /// </summary>
        public int GetRecordCount(string strWhere)
        {
            return dal.GetRecordCount(strWhere);
        }
        /// <summary>
        /// 分页获取数据列表
        /// </summary>
        public DataSet GetListByPage(string strWhere, string orderby, int startIndex, int endIndex)
        {
            return dal.GetListByPage(strWhere, orderby, startIndex, endIndex);
        }
        /// <summary>
        /// 分页获取数据列表
        /// </summary>
        //public DataSet GetList(int PageSize,int PageIndex,string strWhere)
        //{
        //return dal.GetList(PageSize,PageIndex,strWhere);
        //}

        #endregion  BasicMethod
        #region  ExtensionMethod
        public XWEDBAccess.Model.GoodsSiteModel GetModel(string houseName,string gsname)
        {
            string strWhere = " HouseName='"+houseName+"' and GoodsSiteName='" +gsname+"'";
            List<XWEDBAccess.Model.GoodsSiteModel> gsList = GetModelList(strWhere);
            if(gsList!=null&&gsList.Count>0)
            {
                return gsList[0];
            }
            else
            {
                return null;
            }
        }
        public bool UpdateGs(string houseName,string gsName,string operateStatus,string testStaus,string testType)
        {
            XWEDBAccess.Model.GoodsSiteModel gsModel = GetModel(houseName, gsName);
            if(gsModel == null)
            {
                return false;
            }
            gsModel.OperateStatus = operateStatus;
            gsModel.TestStatus = testStaus;
            gsModel.TestType = testType;
            if(this.Update(gsModel)==false)
            {
                return false;
            }
            return true;
        }
        public bool UpdateGs(string houseName, string gsName, string testStaus, string testType)
        {
            XWEDBAccess.Model.GoodsSiteModel gsModel = GetModel(houseName, gsName);
            if (gsModel == null)
            {
                return false;
            }
            gsModel.TestStatus = testStaus;
            gsModel.TestType = testType;
            if (this.Update(gsModel) == false)
            {
                return false;
            }

            return true;
        }
        public bool UpdateGs(string houseName, string gsName, string operateStatus)
        {
            XWEDBAccess.Model.GoodsSiteModel gsModel = GetModel(houseName, gsName);
            if (gsModel == null)
            {
                return false;
            }
            gsModel.OperateStatus = operateStatus;
            if (this.Update(gsModel) == false)
            {
                return false;
            }

            return true;
        }
        /// <summary>
        /// 初始化货位
        /// </summary>
        /// <param name="houseName"></param>
        /// <param name="gsName"></param>
        /// <returns></returns>
        public bool InitGS(string houseName, string gsName, string testType)
        {
            XWEDBAccess.Model.GoodsSiteModel gsModel = GetModel(houseName, gsName);

            XWEDBAccess.Model.GoodsSiteModel newModel = new Model.GoodsSiteModel();
            newModel.HouseName = houseName;
            newModel.GoodsSiteName = gsName;
            newModel.OperateStatus = SysCfg.EnumOperateStatus.空闲.ToString();
            newModel.TestStatus = EnumTestStatus.待测试.ToString();
            newModel.TestType = testType;
            newModel.UpdateTime = DateTime.Parse(DateTime.Now.ToString("yyyy-MM-dd HH:MM:ss"));
            if (gsModel == null)//插入
            {

                if (Add(newModel) <= 0)
                {
                    return false;
                }
            }
            else//更新
            {
                newModel.GoodsSiteID = gsModel.GoodsSiteID;
                if (Update(newModel) == false)
                {
                    return false;
                }
            }
            return true;
        }


        public bool InitGS2(string houseName, string gsName, string testType,string rfid)
        {
            XWEDBAccess.Model.GoodsSiteModel gsModel = GetModel(houseName, gsName);

            XWEDBAccess.Model.GoodsSiteModel newModel = new Model.GoodsSiteModel();
            newModel.HouseName = houseName;
            newModel.GoodsSiteName = gsName;
            newModel.OperateStatus = SysCfg.EnumOperateStatus.空闲.ToString();
            newModel.TestStatus = EnumTestStatus.待测试.ToString();
            newModel.TestType = testType;
            newModel.UpdateTime = DateTime.Parse(DateTime.Now.ToString("yyyy-MM-dd HH:MM:ss"));
            newModel.Tag1 = rfid;
            if (gsModel == null)//插入
            {

                if (Add(newModel) <= 0)
                {
                    return false;
                }
            }
            else//更新
            {
                newModel.GoodsSiteID = gsModel.GoodsSiteID;
                if (Update(newModel) == false)
                {
                    return false;
                }
            }
            return true;
        }
      
        public DataTable GetGsData(string houseName,string gsName)
        {
            string strWhere = "HouseName = '" + houseName + "' ";

            if(gsName.Trim() != "")
            {
                strWhere += " and GoodsSiteName = '" + gsName + "'";
            }
            DataSet ds = dal.GetList(strWhere);
            if(ds!= null&&ds.Tables.Count>0)
            {
                return ds.Tables[0];
            }
            else
            {
                return null;
            }
        }
        #endregion  ExtensionMethod
    }
}

