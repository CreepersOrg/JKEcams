using AsrsModel;
using FlowCtlBaseModel;
using SysCfg;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;
using MesDBAccess.BLL;
using MesDBAccess.Model;
using CtlDBAccess.BLL;
using CtlDBAccess.Model;

namespace AsrsControl
{
    public class XWEProcessModel
    {
        public static CellCoordModel AHouseDCRStation = null;//A库房DCR测试工位
        public static CellCoordModel BHouseDCRStation = null;//B库房DCR测试工位
        private AsrsCtlModel AsrsModel { get; set; }
        private ControlTaskBll bllControlTask = new ControlTaskBll();
        private XWEDBAccess.BLL.GoodsSiteBLL xweGsBll = new XWEDBAccess.BLL.GoodsSiteBLL();
        private XWEDBAccess.BLL.BatteryCodeBLL xweBatteryCodeBll = new XWEDBAccess.BLL.BatteryCodeBLL();
        private XWEDBAccess.BLL.View_GSBatteryBLL xweViewGsBatteryBll = new XWEDBAccess.BLL.View_GSBatteryBLL();
        
        int mesenable = Convert.ToInt32(ConfigurationManager.AppSettings["MESEnable"]);
        private XWEHistroyBLL xweHistroyBLL = new XWEHistroyBLL();
       

        private ThreadBaseModel XWECellMonitorThread = null;
        public XWEProcessModel(AsrsCtlModel asrsCtlModel)
        {
            this.AsrsModel = asrsCtlModel;
            XWEProcessModel.AHouseDCRStation = new CellCoordModel(1, 15, 1);//A库房DCR测试工位
            XWEProcessModel.BHouseDCRStation = new CellCoordModel(1, 1, 1); //B库房DCR测试工位
        }
        #region 公共接口
        /// <summary>
        /// 初始化线程
        /// </summary>
        /// <returns></returns>
        public bool Init()
        {
            //2出入口监控线程
            XWECellMonitorThread = new ThreadBaseModel(this.AsrsModel.HouseName + "新威尔状态监控线程");
            XWECellMonitorThread.LogRecorder = this.AsrsModel.LogRecorder;
            XWECellMonitorThread.LoopInterval = 100;
            XWECellMonitorThread.SetThreadRoutine(AsrsBatteryStatusMonitor);
            if (!XWECellMonitorThread.TaskInit())
            {
                this.AsrsModel.LogRecorder.AddLog(new LogInterface.LogModel(this.AsrsModel.NodeName, "新威尔状态监控线程创建失败", LogInterface.EnumLoglevel.错误));
                return false;
            }
            return true;
        }
        /// <summary>
        /// 暂停
        /// </summary>
        public void PauseRun()
        {
            this.XWECellMonitorThread.TaskPause();
         
        }
        /// <summary>
        /// 启动线程
        /// </summary>
        /// <param name="restr"></param>
        /// <returns></returns>
        public bool StartRun(ref string restr)
        {
            return this.XWECellMonitorThread.TaskStart(ref restr);
        }
        /// <summary>
        /// 退出线程
        /// </summary>
        /// <param name="restr"></param>
        public void ExitRun(ref string  restr)
        {
            this.XWECellMonitorThread.TaskExit(ref restr);
        }

        /// <summary>
        ///  入库A1、B1完成或者在移库完成的时候新威尔的交互流程，这里只处理进入测试区的货位，在暂存区的无需此流程
        ///  入B1库的时候传输工装号及串好串的两个条码,其他流程和A1一样
        /// </summary>
        /// <param name="houseName">库房名称</param>
        /// <param name="goodsSiteName">货位名称</param>
        /// <param name="plletID">托盘号</param>
        /// <param name="batteryIDs">电池数组</param>
        /// <returns>执行状态</returns>
        public bool InHouseTestAreaLogic(string houseName, string goodsSiteName, string plletID,string[] batteryIDs,ref string restr)
        {
            //首先要在goodssite表中插入或更新数据,此处需要判断是那个区，暂存区就不是充放电测试
            if (xweGsBll.InitGS2(houseName, goodsSiteName, EnumTestType.充放电测试.ToString(),plletID) == false)
            {
                this.AsrsModel.LogRecorder.AddDebugLog("威尔流程监控", "新威尔流程入库或者移库流程初始化货位信息失败！");
                return false;
            }
            XWEDBAccess.Model.GoodsSiteModel gsm = xweGsBll.GetModel(houseName, goodsSiteName);
            if(gsm == null)
            {
                this.AsrsModel.LogRecorder.AddDebugLog("威尔流程监控", "货位为空！");
                return false;
            }
            //向中间库中插入托盘条码及电芯条码,同时将有货位的电芯条码清空
            if (xweBatteryCodeBll.AddBattery((int)gsm.GoodsSiteID, batteryIDs) == false)
            {
                this.AsrsModel.LogRecorder.AddDebugLog("威尔流程监控", "向电池条码表中添加数据失败！");
                return false;
            }
            return true;
        }
        /// <summary>
        /// 移库完成逻辑
        /// </summary>
        /// <param name="houseName"></param>
        /// <param name="goodsSiteName"></param>
        /// <param name="restr"></param>
        /// <returns></returns>
        public bool MoveHouseCptLogic(string houseName, string goodsSiteName, string plletID,string[] batteryIDs,ref string restr)
        {
            return InHouseTestAreaLogic(houseName, goodsSiteName, plletID, batteryIDs, ref restr);
          
        }

        ///// <summary>
        ///// 充放电任务完成后的逻辑处理
        ///// </summary>
        ///// <param name="houseName"></param>
        ///// <param name="goodsSiteName"></param>
        ///// <returns></returns>
        //public bool PowerTestCptLogic(string houseName, string goodsSiteName)
        //{
        //    if (xweGsBll.UpdateGs(houseName, goodsSiteName,EnumOperateStatus.空闲.ToString()
        //        ,EnumTestStatus.待测试.ToString(),EnumTestType.无.ToString()) == false)//空货位的时候测试类型为无
        //    {
        //        this.AsrsModel.LogRecorder.AddDebugLog("新威尔流程监控", "充放电完成完成后初始化货位信息失败！");
        //        return false;
        //    }

        //    return true;
        //}
        /// <summary>
        /// DCR出库完成逻辑，即到达DCR测试工位
        /// </summary>
        /// <param name="houseName"></param>
        /// <param name="dcrGsm"></param>
        /// <returns></returns>
        public bool DCROutHouseCpt(string houseName, string powerGsm, string dcrGsm,string rfid)
        {
            if (xweGsBll.UpdateGs(houseName, powerGsm, EnumOperateStatus.空闲.ToString()
               , EnumTestStatus.待测试.ToString(), EnumTestType.无.ToString()) == false)//空货位的时候测试类型为无
            {
                this.AsrsModel.LogRecorder.AddDebugLog("新威尔流程监控", "到达DRC测试工位更新数据失败！");
                return false;
            }
          
            if (xweGsBll.InitGS(houseName, dcrGsm, EnumTestType.DCR测试.ToString()) == false)//空货位的时候测试类型为无
            {
                this.AsrsModel.LogRecorder.AddDebugLog("新威尔流程监控", "到达DRC测试工位创建DCR测试工位数据失败！");
                return false;
            }

            XWEDBAccess.Model.GoodsSiteModel gsm = xweGsBll.GetModel(houseName, powerGsm);
            List<XWEDBAccess.Model.BatteryCodeModel> batteryList = xweBatteryCodeBll.GetModelList("GoodsSiteID = " + gsm.GoodsSiteID + " order by Channel asc");
            string[] batteryCodes = new string[batteryList.Count];
            for (int i = 0; i < batteryList.Count; i++)
            {
                batteryCodes[i] = batteryList[i].Code;
            }
            XWEDBAccess.Model.GoodsSiteModel dcrGsmModel = xweGsBll.GetModel(houseName, dcrGsm);
            dcrGsmModel.Tag1 = rfid;
            xweGsBll.Update(dcrGsmModel);
            //向中间库中插入托盘条码及电芯条码,
            if (xweBatteryCodeBll.AddBattery((int)dcrGsmModel.GoodsSiteID, batteryCodes) == false)
            {
                this.AsrsModel.LogRecorder.AddDebugLog("威尔流程监控", "向电池条码表中添加数据失败！");
                return false;
            }
            return true;
        }
        public bool EmerOutHouseCmpLogic(string houseName,string goodsSiteName)
        {
            //初始化货位
            if (xweGsBll.InitGS(houseName, goodsSiteName,SysCfg.EnumTestType.无.ToString()) == false)//
            {
                this.AsrsModel.LogRecorder.AddDebugLog("新威尔流程监控", "充放电完成完成后初始化货位信息失败！");
                return false;
            }
          
            //XWEDBAccess.Model.GoodsSiteModel gsm = xweGsBll.GetModel(houseName,goodsSiteName);
            //if(gsm == null)
            //{
            //    return false;
            //}
            ////当前货位的电信也删除
            //xweBatteryCodeBll.DeleteByGSID((int)gsm.GoodsSiteID);

            return true;
        }

        public bool DCRTestCptLogic(string houseName)
        {
            //this.AsrsModel.LogRecorder.AddDebugLog("A1库堆垛机", "20");
            string goodsSiteName = "";
            if (houseName == EnumStoreHouse.A1库房.ToString())
            {
                goodsSiteName = AHouseDCRStation.Row.ToString() + "-" + AHouseDCRStation.Col.ToString() + "-" + AHouseDCRStation.Layer.ToString();
            }
            else
            {
                goodsSiteName = BHouseDCRStation.Row.ToString() + "-" + BHouseDCRStation.Col.ToString() + "-" + BHouseDCRStation.Layer.ToString();
            }
            //this.AsrsModel.LogRecorder.AddDebugLog("A1库堆垛机", "21");
            //上报德赛数据
            if (xweGsBll.InitGS(houseName, goodsSiteName, SysCfg.EnumTestType.无.ToString()) == false)//
            {
                this.AsrsModel.LogRecorder.AddDebugLog("新威尔流程监控", "DCR测试完成后更新货位信息失败！");
                return false;
            }
            //this.AsrsModel.LogRecorder.AddDebugLog("A1库堆垛机", "22");
            return true;
        }
    

        #endregion

        #region 库存内部测试完毕自动出库任务流程
        /// <summary>
        /// 监控暂存区、测试区的电池状态生成，移库、出库、紧急出库任务
        /// </summary>
        private void AsrsBatteryStatusMonitor()
        {
            try
            {
                List<XWEDBAccess.Model.GoodsSiteModel> xweGsList = xweGsBll.GetModelList("HouseName = '" 
                    + this.AsrsModel.HouseName + "' and OperateStatus != '锁定' and (TestStatus = '报警' or TestStatus='成功')");

                //List<XWEDBAccess.Model.GoodsSiteModel> xweGsList = xweGsBll.GetModelList("HouseName = '" + this.AsrsModel.HouseName + "' and (TestStatus = '报警' or TestStatus='成功')");

                if (xweGsList == null || xweGsList.Count == 0)
                {
                    return;
                }
                for (int i = 0; i < xweGsList.Count; i++)
                {
                    XWEDBAccess.Model.GoodsSiteModel xmeGs = xweGsList[i];
                    if ( xmeGs.TestStatus.Trim() == SysCfg.EnumTestStatus.成功.ToString())
                    {
                        AutoOutHouseGsTask(xmeGs, (SysCfg.EnumTestType)Enum.Parse(typeof(SysCfg.EnumTestType), xmeGs.TestType));
                    }
                    else if (xmeGs.TestStatus.Trim() == SysCfg.EnumTestStatus.报警.ToString())//报警处理,生成紧急出库任务
                    {
                        AutoEmerOutHouseTask(xmeGs);
                    }
                }
                CreateMoveHouseTask();
            }
            catch (Exception ex)
            {
                Console.WriteLine("电池状态监控错误：" + ex.StackTrace);
            }

        }
        /// <summary>
        /// 紧急任务出库
        /// </summary>
        /// <param name="xweModel"></param>
        /// <returns></returns>
        private bool AutoEmerOutHouseTask(XWEDBAccess.Model.GoodsSiteModel xweModel)
        {
            string[] rcl = xweModel.GoodsSiteName.Split('-');

            CellCoordModel cell = new CellCoordModel(int.Parse(rcl[0]), int.Parse(rcl[1]), int.Parse(rcl[2]));
            if (this.AsrsModel.GenerateOutputTask(cell, null, SysCfg.EnumAsrsTaskType.紧急出库, true) == false)
            {
                this.AsrsModel.LogRecorder.AddDebugLog("库存控制模块", "生成紧急出库任务失败！");
                return false;
            }

            // 上传MES Start
            Report2Mes(xweModel);
            // 上传MES END

            if (xweGsBll.UpdateGs(this.AsrsModel.HouseName, xweModel.GoodsSiteName, EnumOperateStatus.锁定.ToString()) == false)//将出库的货位锁定，根据锁定状态循环生成任务
            {
                this.AsrsModel.LogRecorder.AddDebugLog("库存控制模块", "更新新威尔中间库的锁定状态失败！");
                return false;
            }

            // 将测试结果保存到数据库中 START
            AddXWEHistroy(xweModel);
            // 将测试结果保存到数据库中 END
            return true;
        }


        void Report2Mes(XWEDBAccess.Model.GoodsSiteModel xweModel)
        {
            string[] rcl = xweModel.GoodsSiteName.Split('-');
            if (mesenable != 0)
            {
                List<XWEDBAccess.Model.BatteryCodeModel> batteryList = xweBatteryCodeBll.GetBatteryListData(xweModel.GoodsSiteID);
                if (batteryList != null)
                {
                    foreach (XWEDBAccess.Model.BatteryCodeModel singleBattery in batteryList)
                    {
                        string reports = string.Empty;
                        reports += singleBattery.Code.ToString() + ",";
                        reports += rcl[0] + "-" + rcl[1] + "-" + rcl[2] + ",";
                        reports += singleBattery.Channel + ",";
                        reports += singleBattery.Pressure + ",";
                        reports += singleBattery.InnerRC + ",";
                        reports += singleBattery.Power + ",";
                        reports += singleBattery.Capcity + ",";
                        if (singleBattery.TestResult.ToUpper() == "TRUE" || singleBattery.TestResult.ToUpper() == "OK")
                        {
                            reports += "OK";
                        }
                        else
                        {
                            reports += "NG";
                        }
                        reports += singleBattery.Electricity + ",";
                        reports += singleBattery.Temperature ;

                        if (this.AsrsModel.HouseName == EnumStoreHouse.A1库房.ToString())
                        {
                            WCFClient.MESWCFManage.Inst().UpLoadTestDataA(reports);
                        }
                        else
                        {
                            WCFClient.MESWCFManage.Inst().UpLoadTestDataB(reports);
                        }
                    }
                }
            }
        }

        void AddXWEHistroy(XWEDBAccess.Model.GoodsSiteModel xweModel)
        {
            List<XWEDBAccess.Model.BatteryCodeModel> batteryList = xweBatteryCodeBll.GetBatteryListData(xweModel.GoodsSiteID);
            if (batteryList != null)
            {
                foreach (XWEDBAccess.Model.BatteryCodeModel singleBattery in batteryList)
                {
                    XWEHistroyModel addModel = new XWEHistroyModel();
                    addModel.Code = singleBattery.Code;
                    addModel.Channel = singleBattery.Channel;
                    addModel.Pressure = singleBattery.Pressure;
                    addModel.InnerRC = singleBattery.InnerRC;
                    addModel.Power = singleBattery.Power;
                    addModel.Capcity = singleBattery.Capcity;
                    addModel.TestResult = singleBattery.TestResult;
                    addModel.TestTime = singleBattery.TestTime;
                    addModel.Tag1 = singleBattery.Tag1;
                    addModel.Tag2 = singleBattery.Tag2;
                    addModel.Tag3 = singleBattery.Tag3;
                    addModel.Tag4 = singleBattery.Tag4;
                    addModel.Tag5 = singleBattery.Tag5;
                    addModel.HouseName = xweModel.HouseName;
                    addModel.GoodsSiteName = xweModel.GoodsSiteName;
                    addModel.TestStatus = xweModel.TestStatus;
                    addModel.TestType = xweModel.TestType;
                    addModel.PalletID = xweModel.Tag1;
                    xweHistroyBLL.Add(addModel);
                   
                }
            }


        }

        /// <summary>
        ///DCR出库和产品出库
        /// </summary>
        /// <param name="xweModel"></param>
        /// <param name="testType"></param>
        /// <returns></returns>
        private bool AutoOutHouseGsTask(XWEDBAccess.Model.GoodsSiteModel xweModel, SysCfg.EnumTestType testType)
        {
            string[] rcl = xweModel.GoodsSiteName.Split('-');

            CellCoordModel cell = new CellCoordModel(int.Parse(rcl[0]), int.Parse(rcl[1]), int.Parse(rcl[2]));
            CellCoordModel cell2 = null;
            // 上传MES Start
            Report2Mes(xweModel);
            // 上传MES END

            if (testType == SysCfg.EnumTestType.充放电测试)
            {
                ControlTaskModel dcrWaitOrRunningTask = null;
                if (this.AsrsModel.HouseName == EnumStoreHouse.A1库房.ToString())
                {
                    dcrWaitOrRunningTask=   bllControlTask.GetProcessWaitOrRunningTask(EnumStoreHouse.A1库房.ToString(), 4, "1001");
                     cell2 =AHouseDCRStation;//特殊固定的位置
                }
                else//b1库房
                {
                    dcrWaitOrRunningTask = bllControlTask.GetProcessWaitOrRunningTask(EnumStoreHouse.B1库房.ToString(), 4, "1002");
                     cell2 = BHouseDCRStation;//特殊固定的位置
                }
                if (dcrWaitOrRunningTask!=null)//如果已经有dcr执行中或者待执行的任务就不在生成了，同一个工位的锁定
                {
                    return false;
                }
                //在生成dcr测试的时候要判断下DCR测试工位是否有料，在新威尔交互的数据表中记录dcr工位的测试类型为无的时候证明已经测试完并出库完成
                if (IsDCRStationValid(this.AsrsModel.HouseName) == false)
                {
                    return false;
                }

                if (this.AsrsModel.GenerateOutputTask(cell, cell2, SysCfg.EnumAsrsTaskType.DCR测试, true) == false)
                {
                    this.AsrsModel.LogRecorder.AddDebugLog("库存控制模块", "生成DCR测试任务失败！");
                    return false;
                }
                if (xweGsBll.UpdateGs(this.AsrsModel.HouseName, xweModel.GoodsSiteName, EnumOperateStatus.锁定.ToString()) == false)//将出库的货位锁定，根据锁定状态循环生成任务
                {
                    this.AsrsModel.LogRecorder.AddDebugLog("库存控制模块", "更新新威尔中间库的锁定状态失败！");
                    return false;
                }

                // 将测试结果保存到数据库中 START 
                AddXWEHistroy(xweModel);
                // 将测试结果保存到数据库中 END 

                return true;
            }
            else if (testType == SysCfg.EnumTestType.DCR测试)//正常出库
            {

                ControlTaskModel dcrOutWaitOrRunningTask = null;
                if (this.AsrsModel.HouseName == EnumStoreHouse.A1库房.ToString())
                {
                    dcrOutWaitOrRunningTask = bllControlTask.GetProcessWaitOrRunningTask(EnumStoreHouse.A1库房.ToString(), 3, "1001");
                }
                else//b1库房
                {
                    dcrOutWaitOrRunningTask = bllControlTask.GetProcessWaitOrRunningTask(EnumStoreHouse.B1库房.ToString(), 3, "1002");
                }
                if (dcrOutWaitOrRunningTask != null)//如果已经有dcr执行中或者待执行的任务就不在生成了，同一个工位的锁定
                {
                    return false;
                }
             
                if (this.AsrsModel.GenerateOutputTask(cell, null, SysCfg.EnumAsrsTaskType.DCR出库, true) == false)
                {
                    this.AsrsModel.LogRecorder.AddDebugLog("库存控制模块", "生成DCR测试任务失败！");
                    return false;
                }
                if (xweGsBll.UpdateGs(this.AsrsModel.HouseName, xweModel.GoodsSiteName, EnumOperateStatus.锁定.ToString()) == false)//将出库的货位锁定，根据锁定状态循环生成任务
                {
                    this.AsrsModel.LogRecorder.AddDebugLog("库存控制模块", "更新新威尔中间库的锁定状态失败！");
                    return false;
                }
                return true;
            }
            else
            {
                return false;
            }

        }
        /// <summary>
        /// DCR工位是否可用
        /// </summary>
        /// <returns></returns>
        private bool IsDCRStationValid(string houseName)
        {
            string goodsSiteName = "";
            if (houseName == EnumStoreHouse.A1库房.ToString())
            {
                goodsSiteName = AHouseDCRStation.Row.ToString() + "-" + AHouseDCRStation.Col.ToString() + "-" + AHouseDCRStation.Layer.ToString();
            }
            else
            {
                goodsSiteName = BHouseDCRStation.Row.ToString() + "-" + BHouseDCRStation.Col.ToString() + "-" + BHouseDCRStation.Layer.ToString();
            }
            XWEDBAccess.Model.GoodsSiteModel gsModel = xweGsBll.GetModel(houseName, goodsSiteName);
            if(gsModel==null)
            {
                return true;
            }
            if(gsModel.TestType== EnumTestType.无.ToString())
            {
                return true;
            }
            return false;
                 
        }
        #endregion

        #region 自动生成移库任务
        /// <summary>
        /// 生成移库任务，不需要更新新威尔中间库
        /// </summary>
        private void CreateMoveHouseTask()
        {
            if (!this.AsrsModel.GetNodeEnabled())
            {
                return;
            }

            List<CellCoordModel> cacheCells = new List<CellCoordModel>();

            if (this.AsrsModel.asrsResManage.GetAreaCells(this.AsrsModel.HouseName, EnumLogicArea.暂存区.ToString(), ref cacheCells, true) == false)
            {
                this.AsrsModel.LogRecorder.AddDebugLog("库存控制", "获取暂存区的货位失败!");
                return;
            }
            if (cacheCells == null || cacheCells.Count == 0)//暂存货位没有存放货物
            {
                return;
            }

            //可以生成移库任务,此时找出测试区的空位 
            List<CellCoordModel> testCells = new List<CellCoordModel>();
            if (this.AsrsModel.asrsResManage.GetAreaCells(this.AsrsModel.HouseName, EnumLogicArea.测试区.ToString(), ref testCells, false) == false)
            {
                this.AsrsModel.LogRecorder.AddDebugLog("库存控制", "获取暂存区的货位失败!");
                return;
            }
            if (testCells == null || testCells.Count == 0)//测试区没有空位就不能生成移库任务
            {

                return;
            }

            for (int i = 0; i < testCells.Count; i++)
            {
                CellCoordModel targetCell = testCells[i];
                if (cacheCells.Count <= 0)//缓存已经被分配完毕
                {
                    continue;
                }
                CellCoordModel moveCell = GetClosestCell(cacheCells, targetCell);
                //生成移库任务
                string gsStr = string.Format(this.AsrsModel.HouseName + ":生成移库任务从{0}到{1}!", moveCell.Row.ToString()
                        + "-" + moveCell.Col.ToString() + "-" + moveCell.Layer.ToString(), targetCell.Row.ToString()
                        + "-" + targetCell.Col.ToString() + "-" + targetCell.Layer.ToString());

                if (this.AsrsModel.GenerateOutputTask(moveCell, targetCell, SysCfg.EnumAsrsTaskType.移库, true) == false)
                {
                    this.AsrsModel.LogRecorder.AddDebugLog("库存控制", gsStr + "任务失败！");
                    continue;
                }
                else
                {
                    this.AsrsModel.LogRecorder.AddDebugLog("库存控制", gsStr + "任务成功！");
                   
                }

                cacheCells.Remove(moveCell);
                testCells.RemoveAt(i);
                i--;
            }


            //if (houseName == EnumStoreHouse.A1库房.ToString())
            //{
            //    ports[1].Db1ValsToSnd[1] = 1;
            //}

        }
        /// <summary>
        /// 从缓存区中找出一个最近距离的货位放到测试区
        /// 先找最近的列，然后找最近的层
        /// </summary>
        /// <param name="cells"></param>
        /// <param name="targetCell"></param>
        /// <returns></returns>
        private CellCoordModel GetClosestCell(List<CellCoordModel> cacheCells, CellCoordModel targetCell)
        {
            if (cacheCells == null || targetCell == null)
            {
                return null;
            }

            //var resultCol = (from x in cacheCells.AsParallel() select new { Key = x, Value = Math.Abs(x.Col - targetCell.Col) }).OrderBy(x => x.Value);

            //var resultlayer = (from x in resultCol.AsParallel() select new { Key = x, Value = Math.Abs(x.Key.Layer - targetCell.Layer) }).OrderByDescending(x => x.Value).Take(1);
            //resultlayer.ToList().ForEach(x => Console.Write("列："+x.Key.Key.Col + "层："+ x.Key.Key.Layer));
            CellCoordModel cell = cacheCells[0];//暂时取第一个作为移库起始点
            return cell;
        }
        #endregion


        public string GetPlletID(string houseName, string goodsSiteName)
        {
            XWEDBAccess.Model.GoodsSiteModel gsm = xweGsBll.GetModel(houseName, goodsSiteName);
            if (gsm == null)
            {
                return string.Empty;
            }
            return gsm.Tag1;
        }


        #region 私有
      
        #endregion
    }
}
