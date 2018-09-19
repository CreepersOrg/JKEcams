using System;
using System.Data;
using System.Collections.Generic;
 
namespace XWEDBAccess.BLL
{
    /// <summary>
    /// BatteryCode
    /// </summary>
    public partial class BatteryCodeBLL
    {
        private readonly XWEDBAccess.DAL.BatteryCodeDAL dal = new XWEDBAccess.DAL.BatteryCodeDAL();
        public BatteryCodeBLL()
        { }
        #region  BasicMethod
        /// <summary>
        /// 是否存在该记录
        /// </summary>
        public bool Exists(long BatteryCodeID)
        {
            return dal.Exists(BatteryCodeID);
        }

        /// <summary>
        /// 增加一条数据
        /// </summary>
        public long Add(XWEDBAccess.Model.BatteryCodeModel model)
        {
            return dal.Add(model);
        }

        /// <summary>
        /// 更新一条数据
        /// </summary>
        public bool Update(XWEDBAccess.Model.BatteryCodeModel model)
        {
            return dal.Update(model);
        }

        /// <summary>
        /// 删除一条数据
        /// </summary>
        public bool Delete(long BatteryCodeID)
        {

            return dal.Delete(BatteryCodeID);
        }
        /// <summary>
        /// 删除一条数据
        /// </summary>
        public bool DeleteList(string BatteryCodeIDlist)
        {
            return dal.DeleteList(BatteryCodeIDlist);
        }

        /// <summary>
        /// 得到一个对象实体
        /// </summary>
        public XWEDBAccess.Model.BatteryCodeModel GetModel(long BatteryCodeID)
        {

            return dal.GetModel(BatteryCodeID);
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
        public List<XWEDBAccess.Model.BatteryCodeModel> GetModelList(string strWhere)
        {
            DataSet ds = dal.GetList(strWhere);
            return DataTableToList(ds.Tables[0]);
        }
        /// <summary>
        /// 获得数据列表
        /// </summary>
        public List<XWEDBAccess.Model.BatteryCodeModel> DataTableToList(DataTable dt)
        {
            List<XWEDBAccess.Model.BatteryCodeModel> modelList = new List<XWEDBAccess.Model.BatteryCodeModel>();
            int rowsCount = dt.Rows.Count;
            if (rowsCount > 0)
            {
               XWEDBAccess.Model.BatteryCodeModel model;
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
        /// <summary>
        /// 通过货位编号删除
        /// </summary>
        /// <param name="goodssiteID"></param>
        /// <returns></returns>
        public bool DeleteByGSID(int goodssiteID)
        {
            return dal.DeleteByGSID(goodssiteID);
        }
        /// <summary>
        /// 当前货位原有的电池条码删除，插入新的电池条码
        /// </summary>
        /// <param name="goodssiteID"></param>
        /// <param name="batteryIDs"></param>
        /// <returns></returns>
        public bool AddBattery(int goodssiteID,string [] batteryIDs)
        {
            if(batteryIDs.Length==0)
            {
                return false;
            }
            //DeleteByGSID(goodssiteID);


            for (int i = 0; i < batteryIDs.Length; i++)
            {
                XWEDBAccess.Model.BatteryCodeModel batteryModel = GetModelByCodeAndGSid(goodssiteID, batteryIDs[i]);
                if (batteryModel == null)//插入
                {
                    batteryModel = new Model.BatteryCodeModel();
                    batteryModel.GoodsSiteID = goodssiteID;
                    batteryModel.Code = batteryIDs[i];
                    batteryModel.Channel = (i + 1).ToString();
                    Add(batteryModel);
                }
                else//更新
                {
                    batteryModel = new Model.BatteryCodeModel();
                    batteryModel.GoodsSiteID = goodssiteID;
                    batteryModel.Code = batteryIDs[i];
                    batteryModel.Channel = (i + 1).ToString();
                    Update(batteryModel);
                }


            }
            return true;
        }
        /// <summary>
        /// 得到一个对象实体
        /// </summary>
        public XWEDBAccess.Model.BatteryCodeModel GetModelByCodeAndGSid(long gsID,string Code)
        {
            string strWhere = " GoodsSiteID = " + gsID + " and Code='" + Code + "'";
            List<XWEDBAccess.Model.BatteryCodeModel> batteryList = GetModelList(strWhere);
            if(batteryList!=null&&batteryList.Count>0)
            {
                return batteryList[0];
            }
            else
            {
                return null;
            }
        }
        public DataTable GetBatteryData(int goodsSiteID)
        {
            string strWhere = " GoodsSiteID = " + goodsSiteID;
            DataSet ds = GetList(strWhere);
            if(ds!= null && ds.Tables.Count>0)
            {
                return ds.Tables[0];
            }
            else
            {
                return null;
            }
        }


        public  List<XWEDBAccess.Model.BatteryCodeModel> GetBatteryListData(long goodsSiteID)
        {
            string strWhere = " GoodsSiteID = " + goodsSiteID;
            DataSet ds = GetList(strWhere);
            if (ds != null && ds.Tables.Count > 0)
            {
                return DataTableToList(ds.Tables[0]);   
            }
            else
            {
                return null;
            }
            
        }
		
	    /// <summary>
        /// 得到一个对象实体
        /// </summary>
		public XWEDBAccess.Model.BatteryCodeModel GetModelByCode(string Code)
        {
            return dal.GetModelByCode(Code);
        }
        #endregion  ExtensionMethod
    }
}

