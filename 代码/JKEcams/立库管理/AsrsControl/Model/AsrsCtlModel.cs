using CtlDBAccess.BLL;
using CtlDBAccess.Model;
using AsrsInterface;
using AsrsModel;
using DevInterface;
using LogInterface;
using FlowCtlBaseModel;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SysCfg;
using System.Configuration;
using WCFClient;

namespace AsrsControl
{
    /// <summary>
    /// 立库控制模型，包括堆垛机、出入口等对象。
    /// 功能：实时监测出入口的状态，申请入库任务，调度出入库任务的执行。
    /// </summary>
    public class AsrsCtlModel : CtlNodeBaseModel
    {
        #region 数据
        public float defaultStoreTime = 10.0f;//默认存储时间(小时）
        private string houseName = "";
        private List<AsrsPortalModel> ports;
        private AsrsStackerCtlModel stacker;
        private List<ThreadRunModel> threadList = null;
        public IAsrsManageToCtl asrsResManage = null; //立库管理层接口对象
       // private CtlDBAccess.BLL.ControlTaskBll ctlTaskBll = null;
       // private CtlDBAccess.BLL.BatteryModuleBll batModuleBll = null;
        private XWEDBAccess.BLL.GoodsSiteBLL xweGsBll = new XWEDBAccess.BLL.GoodsSiteBLL();
        private XWEDBAccess.BLL.BatteryCodeBLL xweBatteryCodeBll = new XWEDBAccess.BLL.BatteryCodeBLL();
        private XWEDBAccess.BLL.View_GSBatteryBLL xwrGsBatteryBll = new XWEDBAccess.BLL.View_GSBatteryBLL();

        //private ThreadBaseModel asrsMonitorThread = null;
        private ThreadBaseModel PortMonitorThread = null;
        private ThreadBaseModel stackerPlcCommThread = null; //堆垛机PLC通信线程
       // private bool plcInitFlag = false;
       // private Int64 lastPortPlcStat = 0; //控制立库出入口的plc读写计数，异步通信用
        private IDictionary<SysCfg.EnumAsrsTaskType, DateTime> taskWaitBeginDic = null; //任务按类别等待计时开始
        private IDictionary<SysCfg.EnumAsrsTaskType, TimeSpan> taskWaitingDic = null;//任务按类别等待时间
        private EnumAsrsCheckoutMode asrsCheckoutMode = EnumAsrsCheckoutMode.计时出库; //出库模式
        private int asrsRow = 0;
        private int asrsCol = 0;
        private int asrsLayer = 0;
        private XWEProcessModel XweProcessModel { get; set; }
        #endregion
        #region 公共接口
        public AsrsStackerCtlModel StackDevice { get { return stacker; } set { stacker = value; } }
        public List<AsrsPortalModel> Ports { get { return ports; } set { ports = value; } }
        public IAsrsManageToCtl AsrsResManage { get { return asrsResManage; }}
        public string HouseName { get { return houseName; } set { houseName = value; } }
        public EnumAsrsCheckoutMode AsrsCheckoutMode { get { return asrsCheckoutMode; } set { asrsCheckoutMode = value; } }

        int mesenable = Convert.ToInt32(ConfigurationManager.AppSettings["MESEnable"]);

        public AsrsCtlModel()
        {
            this.XweProcessModel = new XWEProcessModel(this);
        }
        public bool Init()
        {
            ctlTaskBll = new ControlTaskBll();
           // batModuleBll = new CtlDBAccess.BLL.BatteryModuleBll();
         
            //1堆垛机控制线程
            threadList = new List<ThreadRunModel>();
            ThreadRunModel stackerThread = new ThreadRunModel(houseName + "堆垛机控制线程");
            stackerThread.AddNode(this.stacker);
            stackerThread.LogRecorder = this.logRecorder;
            stackerThread.LoopInterval = 100;
           // string reStr = "";
            if(!stackerThread.TaskInit())
            {
                logRecorder.AddLog(new LogInterface.LogModel(nodeName, "堆垛机控制线程创建失败", LogInterface.EnumLoglevel.错误));
                return false;
            }
            threadList.Add(stackerThread);

           //2出入口监控线程
            PortMonitorThread = new ThreadBaseModel(houseName + "堆垛机监控线程");
            PortMonitorThread.LogRecorder = this.logRecorder;
            PortMonitorThread.LoopInterval = 100;
            PortMonitorThread.SetThreadRoutine(PortBusinessLoop);
            if (!PortMonitorThread.TaskInit())
            {
                logRecorder.AddLog(new LogInterface.LogModel(nodeName, "监控线程创建失败", LogInterface.EnumLoglevel.错误));
                return false;
            }
            //3堆垛机通信线程

            if (!SysCfg.SysCfgModel.PlcCommSynMode)
            {
                stackerPlcCommThread = new ThreadBaseModel(houseName + "PLC通信线程");
                stackerPlcCommThread.LogRecorder = this.logRecorder;
                stackerPlcCommThread.LoopInterval = 10;
                stackerPlcCommThread.SetThreadRoutine(PlcCommLoop);
            }
            //this.asrsMonitorThread = new ThreadBaseModel("立库货位状态监控线程");
            //asrsMonitorThread.SetThreadRoutine(AsrsBatteryStatusMonitor);
            //asrsMonitorThread.LoopInterval = 1000;
            //asrsMonitorThread.TaskInit();
            if(this.XweProcessModel.Init()==false)
            {
                logRecorder.AddLog(new LogInterface.LogModel(nodeName, "新威尔状态监控线程创建失败！", LogInterface.EnumLoglevel.错误));
                return false;
            }

            this.stacker.dlgtTaskCompleted = TaskCompletedProcess;

            this.nodeID = this.stacker.NodeID;
            //if(this.mesProcessStepID.Count()>0)//自动出库目前通过新威尔中间库判断
            //{
            //    MesDBAccess.BLL.ProcessStepBll processBll = new MesDBAccess.BLL.ProcessStepBll();
            //    MesDBAccess.Model.ProcessStepModel processModel = processBll.GetModel(this.mesProcessStepID.Last());
            //    if(processModel != null)
            //    {
            //        this.defaultStoreTime = float.Parse(processModel.tag1);
            //    }
            //}
           
            return true;
        }
        public void FillTaskTyps(List<SysCfg.EnumAsrsTaskType> taskTypes)
        {
            taskWaitBeginDic = new Dictionary<SysCfg.EnumAsrsTaskType, DateTime>();
            taskWaitingDic = new Dictionary<SysCfg.EnumAsrsTaskType, TimeSpan>();
            foreach (SysCfg.EnumAsrsTaskType taskType in taskTypes)
            {
                taskWaitBeginDic[taskType] = System.DateTime.Now;
                taskWaitingDic[taskType] = TimeSpan.Zero;
            }
            
        }
        public bool StartRun()
        {
          
            string reStr = "";
            if (!SysCfg.SysCfgModel.PlcCommSynMode)
            {
                if (stackerPlcCommThread.TaskStart(ref reStr))
                {
                    logRecorder.AddLog(new LogInterface.LogModel(nodeName, "PLC通信启动失败," + reStr, LogInterface.EnumLoglevel.错误));
                    return false;
                }
            }
           
            foreach(ThreadRunModel thread in threadList)
            {
                if(!thread.TaskStart(ref reStr))
                {
                    logRecorder.AddLog(new LogInterface.LogModel(nodeName, "启动失败," + reStr, LogInterface.EnumLoglevel.错误));
                    return false;
                }
            }
            if (!PortMonitorThread.TaskStart(ref reStr))
            {
                logRecorder.AddLog(new LogInterface.LogModel(nodeName, "启动失败," + reStr, LogInterface.EnumLoglevel.错误));
                return false;
            }
            if(!this.XweProcessModel.StartRun(ref reStr))
            {
                logRecorder.AddLog(new LogInterface.LogModel(nodeName, "新威尔监控线程启动失败," + reStr, LogInterface.EnumLoglevel.错误));
                return false;
            }
            return true;
        }
        public bool PauseRun()
        {
            if (!SysCfg.SysCfgModel.PlcCommSynMode)
            {
                stackerPlcCommThread.TaskPause();
            }
           
            foreach (ThreadRunModel thread in threadList)
            {
                thread.TaskPause();
               
            }
            PortMonitorThread.TaskPause();
            this.XweProcessModel.PauseRun();
            return true;
        }
        public bool ExitRun()
        {
            string reStr = "";
            foreach (ThreadRunModel thread in threadList)
            {
                thread.TaskExit(ref reStr);

            }
            if (!SysCfg.SysCfgModel.PlcCommSynMode)
            {
                stackerPlcCommThread.TaskExit(ref reStr);
            }
            //asrsMonitorThread.TaskExit(ref reStr);
            this.XweProcessModel.ExitRun(ref reStr);
            return true;
        }
        public void SetAsrsPortPlcRW(IPlcRW plcRW)
        {
            foreach(AsrsPortalModel port in ports)
            {
                port.PlcRW = plcRW;
            }
        }
        public void SetLogrecorder(ILogRecorder logRecorder)
        {
            this.logRecorder = logRecorder;
            this.stacker.LogRecorder = logRecorder;
            foreach(AsrsPortalModel port in ports)
            {
                port.LogRecorder = logRecorder;
            }
        }
        public void SetAsrsMangeInterafce(IAsrsManageToCtl asrsResManage)
        {
            this.asrsResManage = asrsResManage;
            this.stacker.AsrsResManage = asrsResManage;
            string reStr = "";
            if (!this.asrsResManage.GetCellCount(houseName, ref asrsRow, ref asrsCol, ref asrsLayer, ref reStr))
            {
                logRecorder.AddLog(new LogModel(nodeName, string.Format("获取货位数量信息失败,{0}", reStr), EnumLoglevel.错误));
                
            }
        }
        public override bool ExeBusiness(ref string reStr)
        {
            return true;
        }
        public bool AsrsCheckinTaskRequire(AsrsPortalModel port, EnumLogicArea logicArea,SysCfg.EnumAsrsTaskType taskType,string[] palletIDS,ref string reStr)
        {
            try
            {
                //if(port.BindedTaskInput != taskType)
                //{
                //    reStr = "未能匹配的入库任务类型 ";
                //    return false;
                //}
                CellCoordModel requireCell = null;
               
                if (asrsResManage.CellRequire(this.houseName, logicArea.ToString(), ref requireCell, ref reStr))
                {
                    //生成任务
                    ControlTaskModel asrsTask = new ControlTaskModel();
                    asrsTask.DeviceID = this.stacker.NodeID;
                    asrsTask.CreateMode = "自动";
                    asrsTask.CreateTime = System.DateTime.Now;
                    asrsTask.TaskID = System.Guid.NewGuid().ToString();
                    asrsTask.TaskStatus = SysCfg.EnumTaskStatus.待执行.ToString();
                    asrsTask.TaskType = (int)taskType;
                    AsrsTaskParamModel taskParam = new AsrsTaskParamModel();

                    taskParam.CellPos1 = requireCell;
                    taskParam.InputPort = port.PortSeq;
                    // if (taskType == EnumAsrsTaskType.产品入库)
                    // {
                    taskParam.InputCellGoods = palletIDS;
                    //  }
                    asrsTask.TaskParam = taskParam.ConvertoStr(taskType);


                    //申请完成后要锁定货位
                    if (!asrsResManage.UpdateCellStatus(houseName, requireCell, EnumCellStatus.空闲, EnumGSTaskStatus.锁定, ref reStr))
                    {
                        logRecorder.AddDebugLog(nodeName, "更新货位状态失败," + reStr);
                        return false;
                    }
                    if (!asrsResManage.UpdateGSOper(houseName, requireCell, EnumGSOperate.入库, ref reStr))
                    {
                        logRecorder.AddDebugLog(nodeName, "更新货位操作类行失败," + reStr);
                        return false;
                    }
                    else
                    {
                        asrsTask.tag1 = houseName;
                        asrsTask.tag2 = string.Format("{0}-{1}-{2}", requireCell.Row, requireCell.Col, requireCell.Layer);
                        asrsTask.Remark = taskType.ToString();
                        ctlTaskBll.Add(asrsTask);
                        string logInfo = string.Format("生成新的任务:{0},货位：{1}-{2}-{3}", taskType.ToString(), requireCell.Row, requireCell.Col, requireCell.Layer);
                        logRecorder.AddDebugLog(nodeName, logInfo);
                        return true;
                    }
                }
                else
                {
                  
                    return false;
                }
            }
            catch (Exception ex)
            {
                reStr = ex.ToString();
                return false;
            }
        }
        /// <summary>
        /// 更新产品工艺状态信息，出库时更新
        /// </summary>
        /// <param name="containerID"></param>
        //public override void UpdateOnlineProductInfo(string containerID)
        //{
        //    string strSql = string.Format(@"palletID='{0}' and palletBinded=1 ",containerID);
        //    List<MesDBAccess.Model.ProductOnlineModel> products = productOnlineBll.GetModelList(strSql);
        //    if(products != null && products.Count()>0)
        //    {
        //        string nextStepID = "";

        //        int seq = SysCfg.SysCfgModel.stepSeqs.IndexOf(products[0].processStepID);
        //        if(seq<0)
        //        {
        //            Console.WriteLine("工艺路径错误,在UpdateOnlineProductInfo（）发生");
        //            return;
        //        }
        //        bool fndOK = false;
        //        for(int i=0;i<mesProcessStepID.Count();i++)
        //        {
        //            string processStep = mesProcessStepID[i];
        //            int stepSeq = SysCfg.SysCfgModel.stepSeqs.IndexOf(processStep);
        //            if(seq<stepSeq)
        //            {
        //                seq = stepSeq;
        //                fndOK = true;
        //                break;
        //            }

        //        }
        //        if(!fndOK)
        //        {
        //            nextStepID = mesProcessStepID[mesProcessStepID.Count() - 1];
        //        }
        //        else
        //        {
        //            nextStepID = SysCfg.SysCfgModel.stepSeqs[seq];
        //        }
                

        //        foreach(MesDBAccess.Model.ProductOnlineModel product in products)
        //        {
        //            product.processStepID = nextStepID;
        //            product.stationID =nodeID;
        //            productOnlineBll.Update(product);
        //        }
        //    }
        //}
        public override bool BuildCfg(System.Xml.Linq.XElement root, ref string reStr)
        {
            try
            {
                ports = new List<AsrsPortalModel>();
                this.nodeName=root.Attribute("name").Value.ToString();
                this.houseName = this.nodeName;
                IEnumerable<XElement> nodeXEList = root.Elements("Node");
                foreach (XElement el in nodeXEList)
                {
                    string className = (string)el.Attribute("className");
                   
                    if(className == "AsrsControl.AsrsStackerCtlModel")
                    {
                        this.stacker = new AsrsStackerCtlModel();
                        stacker.HouseName = this.houseName;
                        if(!this.stacker.BuildCfg(el,ref reStr))
                        {
                            return false;
                        }
                        this.nodeEnabled = this.stacker.NodeEnabled;
                        this.mesProcessStepID = this.stacker.MesProcessStepID;
                    }
                    else if(className == "AsrsPortalModel.AsrsPortalModel")
                    {
                        AsrsPortalModel port = new AsrsPortalModel(this);
                        if(!port.BuildCfg(el,ref reStr))
                        {
                            return false;
                        }
                        this.ports.Add(port);
                    }
                    else
                    {
                        continue;
                    }
                }
                this.currentStat = new CtlNodeStatus(nodeName);
                this.currentStat.Status = EnumNodeStatus.设备空闲;
                this.currentStat.StatDescribe = "空闲状态";
                return true;
            }
            catch (Exception ex)
            {
                reStr = ex.ToString();
                return false;
            }
        }

        public bool GenerateOutputTask(CellCoordModel cell, CellCoordModel cell2, SysCfg.EnumAsrsTaskType taskType, bool autoTaskMode)
        {
           // throw new NotImplementedException();
          
            ControlTaskModel asrsTask = new ControlTaskModel();
            asrsTask.DeviceID = this.stacker.NodeID;
            if(autoTaskMode)
            {
                asrsTask.CreateMode = "自动";
            }
            else
            {
                asrsTask.CreateMode = "手动";
            }
            asrsTask.CreateTime = System.DateTime.Now;
            asrsTask.TaskID = System.Guid.NewGuid().ToString();
            asrsTask.TaskStatus = SysCfg.EnumTaskStatus.待执行.ToString();
            asrsTask.TaskType = (int)taskType;
            AsrsTaskParamModel taskParam = new AsrsTaskParamModel();
            taskParam.InputPort = 0;
           
            taskParam.CellPos1 = cell;
            taskParam.CellPos2 = cell2;
            List<string> storGoods = new List<string>();
            if (asrsResManage.GetStockDetail(houseName, cell, ref storGoods))
            {
                taskParam.InputCellGoods = storGoods.ToArray();
            }
            List<AsrsPortalModel> validPorts = GetOutPortsOfBindedtask(taskType);
            if(validPorts != null && validPorts.Count()>0)
            {
                taskParam.OutputPort = validPorts[0].PortSeq;
            }
            //if (taskType == EnumAsrsTaskType.空框出库)
            //{
            //    taskParam.OutputPort = 3;
            //}
            //else if (taskType == EnumAsrsTaskType.产品出库)
            //{
            //    taskParam.OutputPort = 2;//默认
               
            //}

            if (taskType == EnumAsrsTaskType.DCR出库 || taskType == EnumAsrsTaskType.DCR测试)
            {
                StringBuilder strBuild = new StringBuilder();
                strBuild.AppendFormat("{0}-{1}-{2}", cell.Row, cell.Col, cell.Layer);

                List<string> pallets = new List<string>();
                string getPallet = this.XweProcessModel.GetPlletID(this.houseName, strBuild.ToString());
                if(getPallet.Length > 0 )
                {
                    pallets.Add(getPallet);
                }
                taskParam.InputCellGoods = pallets.ToArray();
            }

            if(taskType == EnumAsrsTaskType.紧急出库 )
            {
                if (this.houseName == EnumStoreHouse.A1库房.ToString())
                {
                    if (cell.Row == 1 && cell.Col == 15 && cell.Layer == 1)
                    {
                        StringBuilder strBuild = new StringBuilder();
                        strBuild.AppendFormat("{0}-{1}-{2}", cell.Row, cell.Col, cell.Layer);

                        List<string> pallets = new List<string>();
                        string getPallet = this.XweProcessModel.GetPlletID(this.houseName, strBuild.ToString());
                        if (getPallet.Length > 0)
                        {
                            pallets.Add(getPallet);
                        }
                        taskParam.InputCellGoods = pallets.ToArray();
                    }
                }
                else
                {
                    if (cell.Row == 1 && cell.Col == 1 && cell.Layer == 1)
                    {
                        StringBuilder strBuild = new StringBuilder();
                        strBuild.AppendFormat("{0}-{1}-{2}", cell.Row, cell.Col, cell.Layer);

                        List<string> pallets = new List<string>();
                        string getPallet = this.XweProcessModel.GetPlletID(this.houseName, strBuild.ToString());
                        if (getPallet.Length > 0)
                        {
                            pallets.Add(getPallet);
                        }
                        taskParam.InputCellGoods = pallets.ToArray();
                    }
                }
            }

            asrsTask.TaskParam = taskParam.ConvertoStr(taskType);
            //申请完成后要锁定货位
            string reStr = "";
            EnumCellStatus cellStoreStat = EnumCellStatus.空闲;
            EnumGSTaskStatus cellTaskStat = EnumGSTaskStatus.完成;
            this.asrsResManage.GetCellStatus(this.houseName, cell, ref cellStoreStat, ref cellTaskStat);
            if (!asrsResManage.UpdateCellStatus(houseName, cell, cellStoreStat, EnumGSTaskStatus.锁定, ref reStr))
            {
                logRecorder.AddDebugLog(nodeName, "更新货位状态失败," + reStr);
                return false;
            }
           
            if (!asrsResManage.UpdateGSOper(houseName, cell, EnumGSOperate.出库, ref reStr))
            {
                logRecorder.AddDebugLog(nodeName, "更新货位操作类行失败," + reStr);
                return false;
            }
            else
            {
                if (taskType == SysCfg.EnumAsrsTaskType.移库 && cell2 != null)
                {
                    List<string> cellStoreProducts = null;
                    if (!asrsResManage.GetStockDetail(houseName, cell, ref cellStoreProducts))
                    {
                        return false;
                    }
                    if (!asrsResManage.UpdateCellStatus(houseName, cell2, cellStoreStat, EnumGSTaskStatus.锁定, ref reStr))
                    {
                        logRecorder.AddDebugLog(nodeName, "更新货位状态失败," + reStr);
                        return false;
                    }
                    taskParam.InputCellGoods = cellStoreProducts.ToArray();
                    asrsTask.TaskParam = taskParam.ConvertoStr(taskType);
                    asrsTask.tag1 = houseName;
                    asrsTask.tag2 = string.Format("{0}-{1}-{2}", cell.Row, cell.Col, cell.Layer);
                    asrsTask.tag3 = string.Format("{0}-{1}-{2}", cell2.Row, cell2.Col, cell2.Layer);
                    asrsTask.Remark = taskType.ToString();

                    ctlTaskBll.Add(asrsTask);
                    string logInfo = string.Format("生成新的任务:{0},货位：{1}-{2}-{3}到 货位：{4}-{5}-{6}", taskType.ToString(), cell.Row, cell.Col, cell.Layer, cell2.Row, cell2.Col, cell2.Layer);
                    logRecorder.AddDebugLog(nodeName, logInfo);
                }
                else if(taskType == SysCfg.EnumAsrsTaskType.DCR测试 && cell2 != null)
                {
                    asrsTask.tag1 = houseName;
                    asrsTask.tag2 = string.Format("{0}-{1}-{2}", cell2.Row, cell2.Col, cell2.Layer);
                    asrsTask.Remark = taskType.ToString();
                    ctlTaskBll.Add(asrsTask);
                    string logInfo = string.Format("生成新的任务:{0},货位：{1}-{2}-{3}", taskType.ToString(), cell2.Row, cell2.Col, cell2.Layer);
                    logRecorder.AddDebugLog(nodeName, logInfo);
                }
                else if(taskType == SysCfg.EnumAsrsTaskType.紧急出库)
                {
                    asrsTask.tag1 = houseName;
                    asrsTask.tag2 = string.Format("{0}-{1}-{2}", cell.Row, cell.Col, cell.Layer);
                    asrsTask.Remark = taskType.ToString();
                    asrsTask.tag5 = "1";
                    ctlTaskBll.Add(asrsTask);
                    string logInfo = string.Format("生成新的任务:{0},货位：{1}-{2}-{3}", taskType.ToString(), cell.Row, cell.Col, cell.Layer);
                    logRecorder.AddDebugLog(nodeName, logInfo);
                }
                else
                {
                    asrsTask.tag1 = houseName;
                    asrsTask.tag2 = string.Format("{0}-{1}-{2}", cell.Row, cell.Col, cell.Layer);
                    asrsTask.Remark = taskType.ToString();
                    ctlTaskBll.Add(asrsTask);
                    string logInfo = string.Format("生成新的任务:{0},货位：{1}-{2}-{3}", taskType.ToString(), cell.Row, cell.Col, cell.Layer);
                    logRecorder.AddDebugLog(nodeName, logInfo);
                }

            }
            return true;
        }
        public bool GenerateEmerOutputTask(CellCoordModel cell, SysCfg.EnumAsrsTaskType taskType, bool autoTaskMode, int EmerGrade, ref string reStr)
        {
            //zwx,此处需要修改
            //if(this.houseName != EnumStoreHouse.B1库房.ToString())
            //{
            //    reStr = "错误的库房选择";
            //    return false;
            //}
            ControlTaskModel asrsTask = new ControlTaskModel();
            asrsTask.DeviceID = this.stacker.NodeID;
            if (autoTaskMode)
            {
                asrsTask.CreateMode = "自动";
            }
            else
            {
                asrsTask.CreateMode = "手动";
            }
            asrsTask.CreateTime = System.DateTime.Now;
            asrsTask.TaskID = System.Guid.NewGuid().ToString();
            asrsTask.TaskStatus = SysCfg.EnumTaskStatus.待执行.ToString();
            asrsTask.TaskType = (int)taskType;
            AsrsTaskParamModel taskParam = new AsrsTaskParamModel();
            taskParam.InputPort = 0;

            taskParam.CellPos1 = cell;
            List<string> storGoods = new List<string>();
            if (asrsResManage.GetStockDetail(houseName, cell, ref storGoods))
            {
                taskParam.InputCellGoods = storGoods.ToArray();
            }
            if (taskType == SysCfg.EnumAsrsTaskType.DCR出库)
            {
                taskParam.OutputPort = 3;
            }
            else if (taskType == SysCfg.EnumAsrsTaskType.移库)
            {
                taskParam.OutputPort = 3;
            }
            else if(taskType == SysCfg.EnumAsrsTaskType.紧急出库)
            {
                taskParam.OutputPort =4;
            }
           
            else if(taskType == SysCfg.EnumAsrsTaskType.DCR测试)
            { }
            else
            {
                reStr = "不支持的任务类型，" + taskType.ToString();
                return false;
            }
            asrsTask.TaskParam = taskParam.ConvertoStr(taskType);
            //申请完成后要锁定货位
           
            EnumCellStatus cellStoreStat = EnumCellStatus.空闲;
            EnumGSTaskStatus cellTaskStat = EnumGSTaskStatus.完成;
            this.asrsResManage.GetCellStatus(this.houseName, cell, ref cellStoreStat, ref cellTaskStat);
            if (!asrsResManage.UpdateCellStatus(houseName, cell, cellStoreStat, EnumGSTaskStatus.锁定, ref reStr))
            {
                logRecorder.AddDebugLog(nodeName, "更新货位状态失败," + reStr);
                reStr = "更新货位状态失败," + reStr;
                return false;
            }
            if (!asrsResManage.UpdateGSOper(houseName, cell, EnumGSOperate.出库, ref reStr))
            {
                logRecorder.AddDebugLog(nodeName, "更新货位操作类行失败," + reStr);
                reStr = "更新货位操作类行失败," + reStr;
                return false;
            }
            else
            {
                asrsTask.tag1 = houseName;
                asrsTask.tag2 = string.Format("{0}-{1}-{2}", cell.Row, cell.Col, cell.Layer);
                asrsTask.Remark = taskType.ToString();
                asrsTask.tag5= EmerGrade.ToString();
                ctlTaskBll.Add(asrsTask);
                string logInfo = string.Format("生成新的任务:{0},货位：{1}-{2}-{3}", taskType.ToString(), cell.Row, cell.Col, cell.Layer);
                logRecorder.AddDebugLog(nodeName, logInfo);
                return true;
            }
        }
        public AsrsPortalModel GetPortByDeviceID(string devID)
        {
            AsrsPortalModel port = null;
            foreach (AsrsPortalModel schPort in Ports)
            {
                if (schPort.NodeID == devID)
                {
                    return schPort;
                }
            }
            return port;
        }

        public bool GetNodeEnabled()
        {
            return this.nodeEnabled;
        }

        #endregion

        #region 库存内部测试完毕出库任务生成
        /// <summary>
        /// 监控暂存区、测试区的电池状态生成，移库、出库、紧急出库任务
        /// </summary>
        private void AsrsBatteryStatusMonitor()
        {
            try
            {
                List<XWEDBAccess.Model.GoodsSiteModel> xweGsList = xweGsBll.GetModelList("");
                if (xweGsList == null || xweGsList.Count == 0)
                {
                    return;
                }
                for (int i = 0; i < xweGsList.Count; i++)
                {
                    XWEDBAccess.Model.GoodsSiteModel xmeGs = xweGsList[i];
                    if (xmeGs.TestStatus.Trim() == SysCfg.EnumTestStatus.成功.ToString())
                    {
                        AutoOutHouseGsTask(xmeGs, (SysCfg.EnumTestType)int.Parse(xmeGs.TestType));
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
            if (GenerateOutputTask(cell, null, SysCfg.EnumAsrsTaskType.紧急出库, true) == false)
            {
                this.logRecorder.AddDebugLog("库存控制模块", "生成紧急出库任务失败！");
                return false;
            }
            return true;
        }
        /// <summary>
        /// 生成紧急任务，优先级高，可直接出库
        /// </summary>
        /// <param name="xweModel"></param>
        /// <param name="testType"></param>
        /// <returns></returns>
        private bool AutoOutHouseGsTask(XWEDBAccess.Model.GoodsSiteModel xweModel, SysCfg.EnumTestType testType)
        {
            string[] rcl = xweModel.GoodsSiteName.Split('-');

            CellCoordModel cell = new CellCoordModel(int.Parse(rcl[0]), int.Parse(rcl[1]), int.Parse(rcl[2]));

            if (testType == SysCfg.EnumTestType.充放电测试)
            {
                if (this.houseName == EnumStoreHouse.A1库房.ToString())
                {
                    cell = new CellCoordModel(1, 15, 1);//特殊固定的位置
                }
                else//b1库房
                {
                    cell = new CellCoordModel(1, 1, 1);//特殊固定的位置
                }
                CellCoordModel cell2 = new CellCoordModel(int.Parse(rcl[0]), int.Parse(rcl[1]), int.Parse(rcl[2]));
                if (GenerateOutputTask(cell, cell2, SysCfg.EnumAsrsTaskType.DCR测试, true) == false)
                {
                    this.logRecorder.AddDebugLog("库存控制模块", "生成DCR测试任务失败！");
                    return false;
                }
                if(xweGsBll.UpdateGs(this.houseName, xweModel.GoodsSiteName, EnumOperateStatus.锁定.ToString())==false)//将出库的货位锁定，根据锁定状态循环生成任务
                {
                    this.logRecorder.AddDebugLog("库存控制模块", "更新新威尔中间库的锁定状态失败！");
                    return false;
                }
                return true;
            }
            else if (testType == SysCfg.EnumTestType.DCR测试)//正常出库
            {
                if (GenerateOutputTask(cell, null, SysCfg.EnumAsrsTaskType.DCR出库, true) == false)
                {
                    this.logRecorder.AddDebugLog("库存控制模块", "生成DCR测试任务失败！");
                    return false;
                }
                return true;
            }
            else
            {
                return false;
            }

        }
        #endregion

        #region 生成移库任务
        /// <summary>
        /// 生成移库任务，不需要更新新威尔中间库
        /// </summary>
        private void CreateMoveHouseTask()
        {
            if (!this.nodeEnabled)
            {
                return;
            }
         
            List<CellCoordModel> cacheCells = new List<CellCoordModel>();

            if(asrsResManage.GetAreaCells(this.houseName, EnumLogicArea.暂存区.ToString(), ref cacheCells, true)==false)
            {
                this.logRecorder.AddDebugLog("库存控制", "获取暂存区的货位失败!");
                return;
            }
            if(cacheCells== null||cacheCells.Count==0)//暂存货位没有存放货物
            {
                return;
            }

            //可以生成移库任务,此时找出测试区的空位 
            List<CellCoordModel> testCells = new List<CellCoordModel>();
            if (asrsResManage.GetAreaCells(this.houseName, EnumLogicArea.测试区.ToString(), ref testCells, false) == false)
            {
                this.logRecorder.AddDebugLog("库存控制", "获取暂存区的货位失败!");
                return;
            }
            if(testCells== null||testCells.Count ==0)//测试区没有空位就不能生成移库任务
            { 
              
                return;
            }

            for(int i=0;i<testCells.Count;i++)
            {
                CellCoordModel targetCell = testCells[i];
                if(cacheCells.Count<=0)//缓存已经被分配完毕
                {
                    continue;
                }
                CellCoordModel moveCell = GetClosestCell(cacheCells, targetCell);
                //生成移库任务
                string gsStr = string.Format(this.houseName + ":生成移库任务从{0}到{1}!", moveCell.Row.ToString()
                        + "-" + moveCell.Col.ToString() + "-" + moveCell.Layer.ToString(), targetCell.Row.ToString()
                        + "-" + targetCell.Col.ToString() + "-" + targetCell.Layer.ToString());

                if(GenerateOutputTask(moveCell,targetCell,SysCfg.EnumAsrsTaskType.移库,true)==false)
                {
                    this.logRecorder.AddDebugLog("库存控制", gsStr + "任务失败！");
                    continue;
                }
                else
                {
                    this.logRecorder.AddDebugLog("库存控制", gsStr + "任务成功！");
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
        private CellCoordModel GetClosestCell(List<CellCoordModel> cacheCells,CellCoordModel targetCell)
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

        #region 私有




        private void AsrsInportBusiness()
        {
            foreach(AsrsPortalModel port in ports)
            {
                if(port.PortCata == 2)//产品出库的不用管
                {
                    continue;
                }
                SysCfg.EnumAsrsTaskType taskType = port.BindedTaskInput;
                //若入口无RFID，则由外部节点控制入库申请
                if (taskType == SysCfg.EnumAsrsTaskType.产品入库 && port.RfidRW == null)
                {
                    continue;
                }
                if(port.PortinBufCapacity>1 && taskType == SysCfg.EnumAsrsTaskType.产品入库)
                {
                    AsrsInportBusiness2(port);
                    continue;
                }
                if (port.Db2Vals[0] < 2)
                {
                    port.Db1ValsToSnd[0] = 1;
                    port.CurrentStat.Status = EnumNodeStatus.设备空闲;
                    port.CurrentStat.StatDescribe = "空";
                    port.CurrentTaskDescribe = "无入库请求";
                }
              
                if (port.Db1ValsToSnd[0] == 2 || port.Db1ValsToSnd[0] == 4)
                {
                    continue;
                }
                
                if (2 != port.Db2Vals[0])
                {
                    continue;
                }
                port.CurrentStat.Status = EnumNodeStatus.设备使用中;
                port.CurrentStat.StatDescribe = "入库申请";
                if (ExistUnCompletedTask((int)taskType))
                {
                    continue;
                }
                string palletUID = string.Empty;
                //只有产品入库才读RFID
             //   bool unbindMode = SysCfg.SysCfgModel.UnbindMode;
                bool unBindMode = SysCfg.SysCfgModel.UnbindMode;
                //if (this.nodeName == EnumStoreHouse.A1库房.ToString())
                //{
                //    unBindMode = true;
                //}
                if (taskType == SysCfg.EnumAsrsTaskType.产品入库)
                {
                    //SysCfg.SysCfgModel.UnbindMode
                    if (SysCfg.SysCfgModel.UnbindMode)
                    {
                        palletUID = System.Guid.NewGuid().ToString();
                    }
                    else
                    {
                        if (SysCfg.SysCfgModel.SimMode || SysCfg.SysCfgModel.RfidSimMode)
                        {
                            rfidUID = port.SimRfidUID;

                        }
                        else
                        {
                            rfidUID = port.RfidRW.ReadStrData();// port.RfidRW.ReadUID();

                            this.rfidUID = this.rfidUID.TrimEnd('\0');
                            this.rfidUID = this.rfidUID.Trim();

                            if(rfidUID.Length>=9)
                            {
                                rfidUID = rfidUID.Substring(0, 9);
                            }
                          
                        }
                        palletUID = rfidUID;

                    }
                    if (string.IsNullOrWhiteSpace(palletUID))
                    {
                        port.CurrentStat.Status = EnumNodeStatus.无法识别;
                        port.CurrentStat.StatDescribe = "读卡失败";
                        port.CurrentTaskDescribe = "读RFID卡失败";
                        if (port.Db1ValsToSnd[0] != 3)
                        {
                            logRecorder.AddDebugLog(nodeName, "读RFID失败，长度不足9字符");
                        }
                        port.Db1ValsToSnd[0] = 3;
                        continue;
                    }
                    //if(palletUID.Length<9)
                    //{
                    //    if (port.Db1ValsToSnd[0]!=3)
                    //    {
                    //        logRecorder.AddDebugLog(nodeName, "读RFID失败，长度不足9字符");
                    //    }
                    //    port.Db1ValsToSnd[0] = 3;
                    //    continue;
                    //}
                    port.LogRecorder.AddDebugLog(port.NodeName, "读到托盘号:" + palletUID);
                    
                }
    
               // }
                string[] cellGoods = null;
             
                port.PushPalletID(palletUID);
                cellGoods = port.PalletBuffer.ToArray();
                List<MesDBAccess.Model.ProductOnlineModel> productList = this.productOnlineBll.GetModelList(string.Format("palletID='{0}' and palletBinded=1 ", palletUID));
                if (!unBindMode && (productList == null || productList.Count() < 1))
                {
                    //taskType = SysCfg.EnumAsrsTaskType.空;//没有产品原来是空料框，这个项目报警即可
                    this.logRecorder.AddDebugLog("控制层入库申请", this.houseName + ":入库申请,工装板绑定数据为空！");
                }
                else
                {
                   // port.PushPalletID(palletUID);
                    //cellGoods = port.PalletBuffer.ToArray();
                  
                    if (cellGoods == null || cellGoods.Count() < 1)
                    {
                        Console.WriteLine("空cellGoods");
                        continue;
                    }
                    if (!unBindMode)
                    {
                        productList = this.productOnlineBll.GetModelList(string.Format("palletID='{0}' and palletBinded=1 ", cellGoods[0]));
                        if (productList == null || productList.Count() < 1)
                        {
                            if (port.Db1ValsToSnd[0] != 4)
                            {
                                logRecorder.AddDebugLog(port.NodeName, "工装板绑定数据为空："+palletUID);
                            }
                            port.Db1ValsToSnd[0] = 4;
                            port.CurrentStat.Status = EnumNodeStatus.设备故障;
                            port.CurrentStat.StatDescribe = "工装板绑定数据为空";
                            port.CurrentTaskDescribe = "工装板绑定数据为空";
                            continue;


                        }
                    }
                }
                string reStr = "";
               //此处应先在测试区申请，如果测试区没有货位再去暂存区申请
                if(ApplyGoodssite(port,EnumLogicArea.测试区,taskType,cellGoods,ref reStr) == false)
                {
                    if(ApplyGoodssite(port,EnumLogicArea.暂存区,taskType,cellGoods,ref reStr)== false)
                    {
                        port.CurrentTaskDescribe = "货位申请失败！";
                    }
                }
                //申请货位
                //string reStr = "";
                //if (AsrsCheckinTaskRequire(port, EnumLogicArea.暂存区,taskType, cellGoods, ref reStr))
                //{
                //   // port.PalletBuffer.Clear(); //清空入口缓存
                //    if(port.ClearBufPallets(ref reStr))
                //    {
                //        port.Db1ValsToSnd[0] = 2;
                //    }
                //    else
                //    {
                //        logRecorder.AddDebugLog(port.NodeName, "清理入口缓存数据失败" + reStr);
                //    }
                    
                //}
                //else
                //{
                //    if (port.Db1ValsToSnd[0] != 5)
                //    {
                //        string logStr = string.Format("{0}申请失败,因为：{1}", taskType.ToString(), reStr);
                //        logRecorder.AddDebugLog(nodeName, logStr);
                //    }
                //    port.Db1ValsToSnd[0] = 5;

                //}

              
            }
        }
        /// <summary>
        /// 根据库区申请货位
        /// </summary>
        /// <param name="port"></param>
        /// <param name="logicArea"></param>
        /// <param name="taskType"></param>
        /// <param name="palledIDs"></param>
        /// <param name="reStr"></param>
        /// <returns></returns>
        private bool ApplyGoodssite(AsrsPortalModel port, EnumLogicArea logicArea, SysCfg.EnumAsrsTaskType taskType, string[] palledIDs, ref string reStr)
        {
            if (AsrsCheckinTaskRequire(port, logicArea, taskType, palledIDs, ref reStr))
            {
                // port.PalletBuffer.Clear(); //清空入口缓存
                if (port.ClearBufPallets(ref reStr))
                {
                    port.Db1ValsToSnd[0] = 2;
                }
                else
                {
                    logRecorder.AddDebugLog(port.NodeName, "清理入口缓存数据失败" + reStr);
                }

                UpdateOnlineProductInfo(palledIDs[0], port.MesProcessStepID[0]);
                AddProduceRecord(palledIDs[0], port.MesProcessStepID[0]);

                return true;
            }
            else
            {
                if (port.Db1ValsToSnd[0] != 5)
                {
                    string logStr = string.Format("{0}申请失败,因为：{1}", taskType.ToString(), reStr);
                    logRecorder.AddDebugLog(nodeName, logStr);
                }
                port.Db1ValsToSnd[0] = 5;
                return false;
            }
        }

        private void AsrsInportBusiness2(AsrsPortalModel port)
        {
            if(port.PortCata == 2)
            {
                return;
            }
            SysCfg.EnumAsrsTaskType taskType = port.BindedTaskInput;
            port.CurrentTaskDescribe = "";
            //1 入库请求
            AsrsCheckinRequire2(port);
            if (port.Db2Vals[0] < 2)
            {
                port.Db1ValsToSnd[0] = 1;
                port.Db1ValsToSnd[1] = 0;//
                port.CurrentStat.Status = EnumNodeStatus.设备空闲;
                port.CurrentStat.StatDescribe = "空";
                port.CurrentTaskDescribe = "复位：无读卡请求";
            }
            //2 读卡
           
            if (port.Db2Vals[0] == 2 && port.Db1ValsToSnd[0] != 2)
            {
                //读卡
               
                if (SysCfg.SysCfgModel.UnbindMode)
                {
                    this.rfidUID = System.Guid.NewGuid().ToString();
                    port.Db1ValsToSnd[0] = 2;
                }
                else
                {
                    if (SysCfg.SysCfgModel.SimMode || SysCfg.SysCfgModel.RfidSimMode)
                    {
                        this.rfidUID = port.SimRfidUID;
                    }
                    else
                    {
                        this.rfidUID = port.RfidRW.ReadStrData();// port.RfidRW.ReadUID();
                    }
                    this.rfidUID = this.rfidUID.TrimEnd('\0');
                    this.rfidUID = this.rfidUID.Trim();
                    if (string.IsNullOrWhiteSpace(this.rfidUID))
                    {
                        port.CurrentStat.Status = EnumNodeStatus.无法识别;
                        port.CurrentStat.StatDescribe = "读卡失败";
                        port.CurrentTaskDescribe = "读RFID卡失败";
                        if(port.Db1ValsToSnd[0] != 3)
                        {
                           
                            logRecorder.AddDebugLog(nodeName, "读RFID失败");
                        }
                        port.Db1ValsToSnd[0] = 3;
                        return;

                    }
                    else
                    {
                        //if (this.rfidUID.Length > 9)
                        //{
                        //    this.rfidUID = this.rfidUID.Substring(0, 9);
                        //}
                        //if (this.rfidUID.Length < 9)
                        //{
                        //    if (port.Db1ValsToSnd[0] != 3)
                        //    {
                        //        logRecorder.AddDebugLog(nodeName, "读RFID失败，长度不足9字符");
                        //    }
                        //    port.Db1ValsToSnd[0] = 3;
                        //    return;
                        //}
                        port.CurrentTaskDescribe = "RFID读卡完成";
                        //批次判断
                        port.Db1ValsToSnd[0] = 2;
                        port.LogRecorder.AddDebugLog(port.NodeName, "读到托盘号:" + this.rfidUID);
                        List<MesDBAccess.Model.ProductOnlineModel> productList = this.productOnlineBll.GetModelList(string.Format("palletID='{0}' and palletBinded=1 ", this.rfidUID));
                        if (productList == null || productList.Count() < 1)
                        {
                            if (port.Db1ValsToSnd[0] != 4)
                            {
                                logRecorder.AddDebugLog(port.NodeName, "工装板绑定数据为空，" + this.rfidUID);
                            }
                            port.Db1ValsToSnd[0] = 4;
                            port.CurrentStat.Status = EnumNodeStatus.设备故障;
                            port.CurrentStat.StatDescribe = "工装板绑定数据为空";
                            port.CurrentTaskDescribe = "工装板绑定数据为空";
                            return;
                        }

                    }
                }
                
            }
            if (port.PalletBuffer.Count() < 1)
            {
                if (port.Db2Vals[1] == 2 || port.Db2Vals[2] == 2) //入口位置1为空
                {
                    port.CurrentTaskDescribe = string.Format("状态异常：入口缓存数据为空，实际入口位置有料框!");
                }
                
            }
            if (port.Db2Vals[1] != 2 && port.Db2Vals[2] != 2)
            {
                if(port.PalletBuffer.Count()>0)
                {
                    port.CurrentTaskDescribe = string.Format("状态异常：实际入口位置为空，入口缓存数据却不为空!");
                }
               
            }
         
          //  port.RfidUID = this.rfidUID;
            if(!string.IsNullOrWhiteSpace(this.rfidUID))
            {
                if (port.PalletBuffer.Count() < 1 ) //记录缓存为空，并且位置1未检测到料箱
                {
                    if(port.Db2Vals[1] != 2 && port.Db2Vals[2]!=2)
                    {
                        port.Db1ValsToSnd[1] = 2;
                        port.PushPalletID(this.rfidUID);
                        this.rfidUID = "";
                    }
                    
                   
                }
                else //if(port.PalletBuffer.Count()<port.PortinBufCapacity)
                {
                    string preBatch = productOnlineBll.GetBatchNameofPallet(port.PalletBuffer[port.PalletBuffer.Count() - 1]);
                    string curBatch = productOnlineBll.GetBatchNameofPallet(this.rfidUID);
                    if (preBatch == curBatch)
                    {
                        port.Db1ValsToSnd[1] = 2;
                        port.PushPalletID(this.rfidUID);
                        this.rfidUID = "";
                    }
                    else
                    {
                        port.Db1ValsToSnd[1] = 1;
                    }
                }
            }
            

        }

        /// <summary>
        /// 入口请求处理规则2：入口多框一起分批次入库
        /// </summary>
        private void AsrsCheckinRequire2(AsrsPortalModel port)
        {
            SysCfg.EnumAsrsTaskType taskType = port.BindedTaskInput;
            if (taskType != SysCfg.EnumAsrsTaskType.产品入库)
            {
                return;
            }
            //入库申请
            bool asrsCheckinReq = false;
            if (port.Db1ValsToSnd[1] == 1 && port.PalletBuffer.Count() > 0 && port.Db2Vals[1] == 2)
            {
                //不同批，入口位置又信号，缓存有数据
                asrsCheckinReq = true;
            }
            else if ( port.PalletBuffer.Count() == port.PortinBufCapacity && port.Db2Vals[2] == 2)
            {
                asrsCheckinReq = true;
            }
            else if (port.Db2Vals[3] == 2 && port.PalletBuffer.Count() > 0) //手动入库按钮请求
            {
                asrsCheckinReq = true;
            }
            if(!asrsCheckinReq)
            {
                return;
            }
            string[] cellGoods = null;
            cellGoods = port.PalletBuffer.ToArray();
            if (cellGoods == null || cellGoods.Count() < 1)
            {
                return;
            }
            if (!SysCfg.SysCfgModel.UnbindMode)
            {
                List<MesDBAccess.Model.ProductOnlineModel> productList = this.productOnlineBll.GetModelList(string.Format("palletID='{0}' and palletBinded=1 ", cellGoods[0]));
                if (productList == null || productList.Count() < 1)
                {
                    if (port.Db1ValsToSnd[0] != 4)
                    {
                        logRecorder.AddDebugLog(port.NodeName, "工装板绑定数据为空:" + cellGoods[0]);
                    }
                    port.Db1ValsToSnd[0] = 4;
                    port.CurrentStat.Status = EnumNodeStatus.设备故障;
                    port.CurrentStat.StatDescribe = "工装板绑定数据为空";
                    port.CurrentTaskDescribe = "工装板绑定数据为空,入库U任务申请失败";
                    return;
                }
            }
            //cellGoods = new string[] { palletUID };
            string reStr = "";
            if(AsrsCheckinTaskRequire(port,EnumLogicArea.测试区,SysCfg.EnumAsrsTaskType.产品入库,cellGoods,ref reStr))
            {
                //port.PalletBuffer.Clear(); //清空入口缓存
                //if(port.ClearBufPallets(ref reStr))
                //{
                //    if (!string.IsNullOrWhiteSpace(this.rfidUID))
                //    {
                //        port.PushPalletID(this.rfidUID);
                //    }
                //}
                //else
                //{
                //    logRecorder.AddDebugLog(port.NodeName, "清理入口缓存数据失败,"+reStr);
                //}
              if(!port.ClearBufPallets(ref reStr))
              {
                  logRecorder.AddDebugLog(port.NodeName, "清理入口缓存数据失败," + reStr);
              }
            }
            else
            {
                if(port.Db1ValsToSnd[0] != 5)
                {
                    string logStr = string.Format("{0}申请失败,因为：{1}", taskType.ToString(), reStr);
                    logRecorder.AddDebugLog(nodeName, logStr);
                }
                port.Db1ValsToSnd[0] = 5;
              
            }
            //CellCoordModel requireCell = null;
            
            //if (asrsResManage.CellRequire(this.houseName, EnumLogicArea.常温区.ToString(), ref requireCell, ref reStr))
            //{
            //    //生成任务
            //    ControlTaskModel asrsTask = new ControlTaskModel();
            //    asrsTask.DeviceID = this.stacker.NodeID;
            //    asrsTask.CreateMode = "自动";
            //    asrsTask.CreateTime = System.DateTime.Now;
            //    asrsTask.TaskID = System.Guid.NewGuid().ToString();
            //    asrsTask.TaskStatus = SysCfg.EnumTaskStatus.待执行.ToString();
            //    asrsTask.TaskType = (int)taskType;
            //    AsrsTaskParamModel taskParam = new AsrsTaskParamModel();

            //    taskParam.CellPos1 = requireCell;
            //    taskParam.InputPort = port.PortSeq;
            //    // if (taskType == EnumAsrsTaskType.产品入库)
            //    // {
            //    taskParam.InputCellGoods = cellGoods;
            //    //  }
            //    asrsTask.TaskParam = taskParam.ConvertoStr(taskType);


            //    //申请完成后要锁定货位
            //    if (!asrsResManage.UpdateCellStatus(houseName, requireCell, EnumCellStatus.空闲, EnumGSTaskStatus.锁定, ref reStr))
            //    {
            //        logRecorder.AddDebugLog(nodeName, "更新货位状态失败," + reStr);
            //        return;
            //    }
            //    if (!asrsResManage.UpdateGSOper(houseName, requireCell, EnumGSOperate.入库, ref reStr))
            //    {
            //        logRecorder.AddDebugLog(nodeName, "更新货位操作类行失败," + reStr);
            //        return;
            //    }
            //    else
            //    {
            //        asrsTask.tag1 = houseName;
            //        asrsTask.tag2 = string.Format("{0}-{1}-{2}", requireCell.Row, requireCell.Col, requireCell.Layer);
            //        asrsTask.Remark = taskType.ToString();
            //        ctlTaskBll.Add(asrsTask);
                    
            //        string logInfo = string.Format("生成新的任务:{0},货位：{1}-{2}-{3}", taskType.ToString(), requireCell.Row, requireCell.Col, requireCell.Layer);
            //        logRecorder.AddDebugLog(nodeName, logInfo);
            //        port.PalletBuffer.Clear(); //清空入口缓存
            //        if(!string.IsNullOrWhiteSpace(this.rfidUID))
            //        {
            //            port.PushPalletID(this.rfidUID);
            //        }
            //    }
            //}
            //else
            //{
            //    string logStr = string.Format("{0}申请失败,因为：{1}", taskType.ToString(), reStr);
            //    logRecorder.AddDebugLog(nodeName, logStr);
            //    return;
            //}
           // port.PalletBuffer.Clear();
           
        }
        private void EmptyPalletOutputRequire(Dictionary<string, GSMemTempModel> asrsStatDic)
        {
            AsrsPortalModel port = null;
            if(this.houseName== EnumStoreHouse.A1库房.ToString())
            {
                port = ports[1];
            }
            //else if(this.houseName== EnumStoreHouse.C1库房.ToString() || this.houseName== EnumStoreHouse.C2库房.ToString())
            //{
            //    port = ports[2];
            //}
            else
            {
                return;
            }
            if(this.houseName== EnumStoreHouse.A1库房.ToString())
            {
                if (port.Db2Vals[0] == 1)//出口有框，禁止出库
                {
                    return;
                }
                if (port.Db1ValsToSnd[0] == 2) //出库请求已经应答
                {
                    return;
                }
               if(port.Db2Vals[1] != 2) //无空框出库请求
               {
                   return;
               }
            }
            else
            {
                if (port.Db2Vals[1] == 1)//出口有框，禁止出库
                { 
                    return;
                }
                if (port.Db1ValsToSnd[0] == 2)//出库请求已经应答
                {
                    return;
                }
                if (port.Db2Vals[0] != 3) //无空框出库请求
                {
                    return;
                }
               
            }
            bool exitFlag = false;
            int r = 1, c = 1, L = 1;
            for (r = 1; r < asrsRow + 1; r++)
            {
                if (exitFlag)
                {
                    break;
                }
                for (c = 1; c < asrsCol + 1; c++)
                {
                    if (exitFlag)
                    {
                        break;
                    }
                    for (L = 1; L < asrsLayer + 1; L++)
                    {
                        CellCoordModel cell = new CellCoordModel(r, c, L);
                        string strKey = string.Format("{0}:{1}-{2}-{3}", houseName, r, c, L);
                        GSMemTempModel cellStat = null;
                        if (!asrsStatDic.Keys.Contains(strKey))
                        {
                            continue;
                        }
                        cellStat = asrsStatDic[strKey];

                        if (cellStat.GSStatus != EnumCellStatus.空料框.ToString())
                        {
                            continue;
                        }

                        if (cellStat.GSTaskStatus != EnumGSTaskStatus.锁定.ToString() && cellStat.GSEnabled)
                        {
                            if (GenerateOutputTask(cell, null, SysCfg.EnumAsrsTaskType.空框出库, true))
                            {
                                exitFlag = true;
                                port.Db1ValsToSnd[0] = 2;
                                string reStr = "";
                                if (!port.NodeCmdCommit(true, ref reStr))
                                {
                                    logRecorder.AddDebugLog(port.NodeName, "发送命令失败" + reStr);
                                }
                                else
                                {
                                    return;
                                }
                            }
                        }
                    }
                }
            }
           
        }
        //private void AsrsInputRequire(string portDevID)
        //{
        //    List<string> inputPorts = new List<string>();
        //    inputPorts.AddRange(new string[]{"2001","2003","2005","2007","2009","2011"});
        //    if(!inputPorts.Contains(portDevID))
        //    {
        //        return;
        //    }
        //    AsrsPortalModel port = GetPortByDeviceID(portDevID);
        //    if(!port.NodeEnabled)
        //    {
        //        return;
        //    }
        //    EnumAsrsTaskType taskType = EnumAsrsTaskType.产品入库;
        //    if (portDevID == "2007" || portDevID=="2011")  //zwx,此处需要修改
        //    {
        //        taskType = EnumAsrsTaskType.空框入库;
        //    }
        //    if(port.Db2Vals[0] < 2)
        //    {
        //        port.Db1ValsToSnd[0] = 1;
        //        port.CurrentStat.Status = EnumNodeStatus.设备空闲;
        //        port.CurrentStat.StatDescribe = "空";
        //        port.CurrentTaskDescribe = "无入库请求";
        //    }
        //    if(port.Db1ValsToSnd[0] == 2 || port.Db1ValsToSnd[0] == 4 || port.Db1ValsToSnd[0] == 5)
        //    {
        //        return;
        //    }
        //    if(2 != port.Db2Vals[0])
        //    {
        //        return;
        //    }
        //    port.CurrentStat.Status = EnumNodeStatus.设备使用中;
        //    port.CurrentStat.StatDescribe = "入库申请";
        //    if (ExistUnCompletedTask((int)taskType))
        //    {
        //        return;
        //    }
        //    //读卡
        //    string palletUID = string.Empty;
        //    if (taskType == EnumAsrsTaskType.产品入库)
        //    {
               
        //        //只有产品入库才读RFID
        //        if(SysCfg.SysCfgModel.UnbindMode)
        //        {
        //            palletUID = System.Guid.NewGuid().ToString();
        //        }
        //        else
        //        {
        //            if (!SysCfg.SysCfgModel.SimMode)
        //            {
        //                rfidUID = port.RfidRW.ReadUID();

        //            }
        //            else
        //            {
        //                rfidUID = port.SimRfidUID;

        //            }
        //            palletUID = rfidUID;
                  
        //        }
                
              
        //    }
        //    string[] cellGoods = null;
        //    if (taskType == EnumAsrsTaskType.产品入库)
        //    {
        //        if (string.IsNullOrWhiteSpace(palletUID))
        //        {
        //            port.CurrentStat.Status = EnumNodeStatus.无法识别;
        //            port.CurrentStat.StatDescribe = "读卡失败";
        //            port.CurrentTaskDescribe = "读RFID卡失败";
        //            port.Db1ValsToSnd[0] = 3;
        //            return;
        //        }
        //        if(!SysCfg.SysCfgModel.UnbindMode)
        //        {
        //            List<MesDBAccess.Model.ProductOnlineModel> productList = this.productOnlineBll.GetModelList(string.Format("palletID='{0}' and palletBinded=1 ", palletUID));
        //            if (productList == null || productList.Count() < 1)
        //            {
        //                if (port.Db1ValsToSnd[0] != 4)
        //                {
        //                    logRecorder.AddDebugLog(port.NodeName, "工装板绑定数据为空");
        //                }
        //                port.Db1ValsToSnd[0] = 4;
        //                port.CurrentStat.Status = EnumNodeStatus.设备故障;
        //                port.CurrentStat.StatDescribe = "工装板绑定数据为空";
        //                port.CurrentTaskDescribe = "工装板绑定数据为空";
        //                return;
        //            }
        //        }
               
        //        cellGoods = new string[]{palletUID};
               
                
        //    }
           
        //    //申请货位
        //    CellCoordModel requireCell = null;
        //    string reStr="";
        //    if(asrsResManage.CellRequire(this.houseName,EnumLogicArea.常温区.ToString(),ref requireCell,ref reStr))
        //    {
        //        //生成任务
        //        ControlTaskModel asrsTask = new ControlTaskModel();
        //        asrsTask.DeviceID = this.stacker.NodeID;
        //        asrsTask.CreateMode = "自动";
        //        asrsTask.CreateTime = System.DateTime.Now;
        //        asrsTask.TaskID = System.Guid.NewGuid().ToString();
        //        asrsTask.TaskStatus = EnumTaskStatus.待执行.ToString();
        //        asrsTask.TaskType = (int)taskType;
        //        AsrsTaskParamModel taskParam = new AsrsTaskParamModel();
                
        //        taskParam.CellPos1 = requireCell;
        //        if(taskType == EnumAsrsTaskType.产品入库)
        //        {
        //            taskParam.InputPort = 1;
        //            taskParam.InputCellGoods = cellGoods;// new string[] { "模组条码1", "条码2" };
        //        }
        //        else if(taskType== EnumAsrsTaskType.空框入库)
        //        {
        //            taskParam.InputPort = 3;
        //        }
        //        asrsTask.TaskParam = taskParam.ConvertoStr(taskType);
               
               
        //        //申请完成后要锁定货位
        //        if (!asrsResManage.UpdateCellStatus(houseName, requireCell, EnumCellStatus.空闲, EnumGSTaskStatus.锁定,ref reStr))
        //        {
        //            logRecorder.AddDebugLog(nodeName, "更新货位状态失败," + reStr);
        //            return;
        //        }
        //        if(!asrsResManage.UpdateGSOper(houseName,requireCell,EnumGSOperate.入库,ref reStr))
        //        {
        //            logRecorder.AddDebugLog(nodeName, "更新货位操作类行失败," + reStr);
        //            return;
        //        }
        //        else
        //        {
        //            asrsTask.tag1 = houseName;
        //            asrsTask.tag2 = string.Format("{0}-{1}-{2}", requireCell.Row, requireCell.Col, requireCell.Layer);
        //            asrsTask.Remark = taskType.ToString();
        //            ctlTaskBll.Add(asrsTask);
        //            port.Db1ValsToSnd[0] = 2;
        //            string logInfo = string.Format("生成新的任务:{0},货位：{1}-{2}-{3}", taskType.ToString(), requireCell.Row, requireCell.Col, requireCell.Layer);
        //            logRecorder.AddDebugLog(nodeName, logInfo);
        //        }
        //    }
        //    else
        //    {
        //        string logStr = string.Format("{0}申请失败,因为：{1}", taskType.ToString(), reStr);
        //        logRecorder.AddDebugLog(nodeName, logStr);
        //        return;
        //    }
        //    //申请成后，应答
        //    port.Db1ValsToSnd[0] = 2;
            
        //}
        //private void AsrsInputRequire_2001()
        //{
        //    AsrsPortalModel port = GetPortByDeviceID("2001");
          
        //    if (2 == port.Db2Vals[0])
        //    {
        //        if(ExistUnCompletedTask((int)EnumAsrsTaskType.产品入库))
        //        {
        //            return;
        //        }
        //        //读卡
        //        string palletUID = port.RfidRW.ReadUID(); 
        //        if(string.IsNullOrWhiteSpace(palletUID))
        //        {
        //            return;
        //        }
        //        //申请货位
        //        CellCoordModel requireCell = null;
        //        string reStr="";
        //        if(asrsResManage.CellRequire(this.houseName,ref requireCell,ref reStr))
        //        {
        //            //生成任务
        //            ControlTaskModel asrsTask = new ControlTaskModel();
        //            asrsTask.DeviceID = this.stacker.NodeID;
        //            asrsTask.CreateMode = "自动";
        //            asrsTask.TaskID = System.Guid.NewGuid().ToString();
        //            asrsTask.TaskStatus = EnumTaskStatus.待执行.ToString();
        //            asrsTask.TaskType = (int)(EnumAsrsTaskType.产品入库);
        //            AsrsTaskParamModel taskParam = new AsrsTaskParamModel();
        //            taskParam.CellPos1 = requireCell;
        //            //查询产品绑定数据库，根据RFID查询条码
        //            taskParam.InputCellGoods = new string[]{"模组条码1","条码2"};
        //            asrsTask.TaskParam = taskParam.ConvertoStr(EnumAsrsTaskType.产品入库);

        //            ctlTaskBll.Add(asrsTask);
        //        }
        //        else
        //        {
        //            logRecorder.AddDebugLog(nodeName, "申请产品入库失败" + reStr);
        //            return;
        //        }
        //        //申请成后，应答
        //        port.Db1ValsToSnd[0] = 2;
        //    }
        //}
        //private void AsrsInputRequire_2003()
        //{
        //    AsrsPortalModel port2003 = GetPortByDeviceID("2003");
        //    //空框入库
        //    AsrsPortalModel port = GetPortByDeviceID("2003");
        //    if (2 == port.Db2Vals[0])
        //    {
        //        if (ExistUnCompletedTask((int)EnumAsrsTaskType.空框入库))
        //        {
        //            return;
        //        }
        //        //申请货位
        //        CellCoordModel requireCell = null;
        //        string reStr="";
        //        if(asrsResManage.CellRequire(this.houseName,ref requireCell,ref reStr))
        //        {
        //            //生成任务
        //            ControlTaskModel asrsTask = new ControlTaskModel();
        //            asrsTask.DeviceID = this.stacker.NodeID;
        //            asrsTask.CreateMode = "自动";
        //            asrsTask.TaskID = System.Guid.NewGuid().ToString();
        //            asrsTask.TaskStatus = EnumTaskStatus.待执行.ToString();
        //            asrsTask.TaskType = (int)(EnumAsrsTaskType.空框入库);
        //            AsrsTaskParamModel taskParam = new AsrsTaskParamModel();
        //            taskParam.CellPos1 = requireCell;
        //            asrsTask.TaskParam = taskParam.ConvertoStr(EnumAsrsTaskType.空框入库);

        //            ctlTaskBll.Add(asrsTask);
        //        }
        //        else
        //        {
        //            logRecorder.AddDebugLog(nodeName, "申请空框入库失败" + reStr);
        //            return;
        //        }
        //        //申请成后，应答
        //        port.Db1ValsToSnd[0] = 2;
        //    }
        //}
      
        private void PortBusinessLoop()
        {
         
            try
            {
                //zwx,此处需要修改
                if(!this.nodeEnabled)
                {
                    return;
                }
                
               // IPlcRW plcRW = ports[0].PlcRW;
                //if (!SysCfgModel.SimMode)
                //{
                //    if (lastPortPlcStat == plcRW.PlcStatCounter)
                //    {
                //        return;
                //    }
                //}

                string reStr = "";
                for (int i = 0; i < ports.Count(); i++)
                {
                    AsrsPortalModel port = ports[i];
                    if (!port.ReadDB2(ref reStr))
                    {
                        logRecorder.AddDebugLog(port.NodeName, "读DB2数据错误");
                        continue;
                    }
                    if (!port.NodeCmdCommit(false, ref reStr))
                    {
                        logRecorder.AddDebugLog(port.NodeName, "数据提交错误");
                        continue;
                    }
                }
               //出口状态
                //string[] outPorts = new string[] { "2002", "2004", "2012","2013" };
                foreach (AsrsPortalModel port in ports)
                {
                    if(port.PortCata ==1)
                    {
                        continue;
                    }
                    if(port.Db2Vals[0] ==2)
                    {
                        port.CurrentStat.StatDescribe = "允许出库";
                        port.CurrentStat.Status = EnumNodeStatus.设备空闲;
                    }
                    else
                    {
                        port.CurrentStat.StatDescribe = "禁止出库";
                        port.CurrentStat.Status = EnumNodeStatus.设备使用中;
                    }
                }
                
                //1 查询各入口是否有入库申请，注意：申请过的就不要申请，防止重复申请。
                AsrsInportBusiness();

                //2 若堆垛机处于空闲状态，根据调度规则取任务
                AsrsTaskAllocate();// 堆垛机作业调度
                //lastPortPlcStat = plcRW.PlcStatCounter;
            }
            catch (Exception ex)
            {
                ThrowErrorStat("异常发生:" + ex.ToString(), EnumNodeStatus.设备故障);
            }
           
        }
        private void PlcCommLoop()
        {
            //if (!plcInitFlag)
            //{
            //    //创建PLC通信对象，连接PLC

            //}
          //  plcRW = this.plcRWs[0] as PLCRWMx;
            //short[] tempDb1Vals = new short[800];
            //if (!plcRW.ReadMultiDB("D2000", 800, ref PLCRWMx.db1Vals))
            //{
            //    this.PauseRun();
            //    logRecorder.AddLog(new LogModel(objectName, "PLC通信失败,系统将停止!", EnumLoglevel.错误));
            //    return;
            //}
            //Array.Copy(tempDb1Vals, PLCRWMx.db1Vals, tempDb1Vals.Count());
            IPlcRW plcRW = stacker.PlcRW;
            DateTime commSt = System.DateTime.Now;
            //if (!plcRW.WriteDB("D2700", 1))
            //{
            //    Console.WriteLine("PLC通信失败!");
            //    //logRecorder.AddLog(new LogModel(objectName, "PLC通信失败!", EnumLoglevel.错误));
            //    string reStr = "";
            //    plcRW.CloseConnect();
            //    if (!plcRW.ConnectPLC(ref reStr))
            //    {
            //        //logRecorder.AddLog(new LogModel(objectName, "PLC重新连接失败!", EnumLoglevel.错误));
            //        Console.WriteLine("PLC重新连接失败!");

            //        return;
            //    }
            //    else
            //    {
            //        logRecorder.AddLog(new LogModel(stacker.NodeName, "PLC重新连接成功!", EnumLoglevel.错误));
            //        return;
            //    }
            //}
            short[] tempDb2Vals = new short[stacker.Db2Vals.Count()];
            if (!plcRW.ReadMultiDB(stacker.Db2StartAddr, stacker.Db2Vals.Count(), ref tempDb2Vals))
            {

                // logRecorder.AddLog(new LogModel(objectName, "PLC通信失败!", EnumLoglevel.错误));
                Console.WriteLine("PLC通信失败!");
                string reStr = "";
                plcRW.CloseConnect();
                if (!plcRW.ConnectPLC(ref reStr))
                {
                    // logRecorder.AddLog(new LogModel(objectName, "PLC重新连接失败!", EnumLoglevel.错误));
                    Console.WriteLine("PLC重新连接失败!");
                   
                    return;
                }
                else
                {
                    logRecorder.AddLog(new LogModel(stacker.NodeName, "PLC重新连接成功!", EnumLoglevel.错误));
                    return;
                }

            }
            Array.Copy(tempDb2Vals, plcRW.Db2Vals, tempDb2Vals.Count());

            short[] tempDB1ValsSnd = new short[stacker.Db1ValsToSnd.Count()];
            Array.Copy(plcRW.Db1Vals, tempDB1ValsSnd, tempDB1ValsSnd.Count());
            if (!plcRW.WriteMultiDB(stacker.Db1StartAddr, stacker.Db1ValsToSnd.Count(), plcRW.Db1Vals))
            {

                //logRecorder.AddLog(new LogModel(objectName, "PLC通信失败!", EnumLoglevel.错误));
                Console.WriteLine("PLC重新连接失败!");
                string reStr = "";
                plcRW.CloseConnect();
                if (!plcRW.ConnectPLC(ref reStr))
                {
                    //logRecorder.AddLog(new LogModel(objectName, "PLC重新连接失败!", EnumLoglevel.错误));
                    Console.WriteLine("PLC重新连接失败!");
                    return;
                }
                else
                {
                    logRecorder.AddLog(new LogModel(stacker.NodeName, "PLC重新连接成功!", EnumLoglevel.错误));
                    return;
                }

            }
            plcRW.PlcRWStatUpdate();
            DateTime commEd = System.DateTime.Now;
            TimeSpan ts = commEd - commSt;
            string dispCommInfo = string.Format("PLC通信周期:{0}毫秒", (int)ts.TotalMilliseconds);
            if (ts.TotalMilliseconds > 500)
            {
                logRecorder.AddDebugLog(stacker.NodeName, dispCommInfo);
            }
           // view.DispCommInfo(dispCommInfo);
        }
        
        /// <summary>
        /// 任务调度
        /// </summary>
        private void AsrsTaskAllocate()
        {
            //zwx,此处需要修改
          //  if(this.houseName == EnumStoreHouse.B1库房.ToString())
            {
                
                //先查询有无紧急任务，
                List<ControlTaskModel> emerTaskList = ctlTaskBll.GetEmerTaskToRunList(SysCfg.EnumTaskStatus.待执行.ToString(), stacker.NodeID);
                if (emerTaskList != null && emerTaskList.Count > 0)
                {
                    List<AsrsPortalModel> validPorts = GetOutPortsOfBindedtask(SysCfg.EnumAsrsTaskType.紧急出库);
                    AsrsPortalModel port = null;
                    if (validPorts != null && validPorts.Count() > 0)
                    {
                       port=validPorts[0];
                    }
                    
                    if (port.Db2Vals[0] != 2)
                    {
                        return;
                    }
                    ControlTaskModel task = emerTaskList[0];
                    if (stacker.CurrentTask == null && stacker.Db2Vals[1] == 1)
                    {
                        string reStr = "";
                        if (stacker.FillTask(task, ref reStr))
                        {
                            return;
                        }
                        else
                        {
                            logRecorder.AddDebugLog(nodeName, "分配任务失败," + "," + reStr);
                        }
                    }

                    return;
                }
            }
           
            //1 先计时，如果当前类型任务正在执行，则不计时
            foreach (SysCfg.EnumAsrsTaskType taskType in taskWaitBeginDic.Keys.ToArray())
            //for (int i = 0; i < taskWaitBeginDic.Keys.Count();i++ ) 
            {
              //  EnumAsrsTaskType taskType = taskWaitBeginDic.Keys[i] as EnumAsrsTaskType;
                if (this.stacker.CurrentTask != null && stacker.CurrentTask.TaskType == (int)taskType)
                {
                    taskWaitBeginDic[taskType] = System.DateTime.Now;
                    taskWaitingDic[taskType] = TimeSpan.Zero;
                }
                else
                {
                    taskWaitingDic[taskType] = System.DateTime.Now - taskWaitBeginDic[taskType];
                }
            }
            //2排序
          
            Dictionary<SysCfg.EnumAsrsTaskType, TimeSpan> dicSortDesc = taskWaitingDic.OrderByDescending(o => o.Value).ToDictionary(o => o.Key, p => p.Value);
         
            //foreach (KeyValuePair<EnumAsrsTaskType, TimeSpan> kvp in dicSortDesc)
            //{
            //    Console.WriteLine("{0} 等待时间{1} 毫秒", kvp.Key, (int)kvp.Value.TotalMilliseconds);
            //}
        
            //3 按照顺序取任务，若当前条件不满足，取下一种任务类型
            if(stacker.CurrentTask == null && stacker.Db2Vals[1] == 1)
            {
            
                //设备当前任务为空，并且空闲，取新的任务
              
                foreach (SysCfg.EnumAsrsTaskType taskType in dicSortDesc.Keys.ToArray())
                {
                    //ControlTaskModel task = ctlTaskBll.GetTaskToRun((int)taskType, EnumTaskStatus.待执行.ToString(),stacker.NodeID);
                   

                    //遍历所有可执行任务，找到第一个可用的
                    List<ControlTaskModel> taskList = ctlTaskBll.GetTaskToRunList((int)taskType, SysCfg.EnumTaskStatus.待执行.ToString(), stacker.NodeID);
                    ControlTaskModel task = null;
                    if(taskList != null)
                    {
                     
                        foreach(ControlTaskModel t in taskList)
                        {
                            //Console.WriteLine("tasktype:" + t.TaskType.ToString());
                            if(t.TaskType == 3)//产品出库，产品出库为DCR测试工位到正常出库口，dcr工位在库房中，但是没有货位信息
                            {
                                task = t;
                                break;
                            }
                            string reStr = "";
                            AsrsTaskParamModel paramModel = new AsrsTaskParamModel();

                            if (!paramModel.ParseParam((SysCfg.EnumAsrsTaskType)taskType, t.TaskParam,houseName, ref reStr))
                            {
                               
                                continue;
                            }
                            EnumGSEnabledStatus cellEnabledStatus = EnumGSEnabledStatus.启用;
                            if (!this.asrsResManage.GetCellEnabledStatus(houseName, paramModel.CellPos1, ref cellEnabledStatus))
                            {
                               
                                // reStr = "获取货位启用状态失败";
                                continue;
                            }
                            if (cellEnabledStatus == EnumGSEnabledStatus.禁用)
                            {
                              
                                continue;
                            }
                            else
                            {
                                task = t;
                                break;
                            }
                        }
                    }
                    if(task != null)
                    {
                      
                        string reStr = "";
                        AsrsTaskParamModel paramModel = new AsrsTaskParamModel();
                       
                        if (!paramModel.ParseParam((SysCfg.EnumAsrsTaskType)taskType, task.TaskParam, houseName, ref reStr))
                        {
                            continue;
                        }
                     
                        EnumGSEnabledStatus cellEnabledStatus = EnumGSEnabledStatus.启用;
                        if (task.TaskType != 3)//3产品出库的是从DCR测试工位出库不需要判断货位
                        {
                            if (!this.asrsResManage.GetCellEnabledStatus(houseName, paramModel.CellPos1, ref cellEnabledStatus))
                            {
                                // reStr = "获取货位启用状态失败";
                                continue;
                            }
                        }
                        if(cellEnabledStatus == EnumGSEnabledStatus.禁用)
                        {
                            continue;
                        }
                        if (   taskType == SysCfg.EnumAsrsTaskType.DCR出库
                            || taskType == SysCfg.EnumAsrsTaskType.紧急出库
                            || taskType == SysCfg.EnumAsrsTaskType.DCR测试)
                        {
                          
                           
                            List<AsrsPortalModel> validPorts = GetOutPortsOfBindedtask(taskType);
                            AsrsPortalModel port = null;
                            if (validPorts != null && validPorts.Count() > 0)
                            {
                               
                                
                                port = validPorts[0];
                              
                                //if(port.PortCata == 3)
                                //{
                                //    //出入库共用一个口
                                //    if (port.Db2Vals[1] != 2)
                                //    {
                                //        continue;
                                //    }
                                //}
                                //else
                                //{
                                    //仅限出口
                              
                                    if (port.Db2Vals[0] != 2)
                                    {
                                        continue;
                                    }
                                  
                                //}
                                
                            }
                            else
                            {
                                continue;
                            }
                        }
                        //else if( taskType == SysCfg.EnumAsrsTaskType.DCR出库)
                        //{
                        //    List<AsrsPortalModel> validPorts = GetOutPortsOfBindedtask(taskType);
                        //    AsrsPortalModel port = null;
                        //    if (validPorts != null && validPorts.Count() > 0)
                        //    {
                        //        port = validPorts[0];
                        //        //if(port.PortCata == 3)
                        //        //{
                        //        //    //出入库共用一个口
                        //        //    if (port.Db2Vals[1] != 2)
                        //        //    {
                        //        //        continue;
                        //        //    }
                        //        //}
                        //        //else
                        //        //{
                        //        //仅限出口
                        //        if (port.Db2Vals[0] != 2)
                        //        {
                        //            continue;
                        //        }
                        //        //}

                            //}
                            //else
                            //{
                            //    continue;
                            //}
                        //}
                        //else if (taskType == SysCfg.EnumAsrsTaskType.紧急出库)
                        //{
                        //}


                    
                        if(stacker.FillTask(task,ref reStr))
                        {
                            break;
                        }
                        else
                        {
                            logRecorder.AddDebugLog(nodeName, "分配任务失败," + taskType.ToString() + "," + reStr);
                        }
                    }
                }
               
            }
           
        }
        ///// <summary>
        ///// 查询是否存在未完成的任务，包括待执行的
        ///// </summary>
        ///// <param name="taskType"></param>
        ///// <returns></returns>
        //private bool ExistUnCompletedTask(int taskType)
        //{
        //    string strWhere = string.Format("TaskType={0} and DeviceID='{1}' and TaskStatus<>'{2}' and TaskStatus<>'{3}'",
        //        taskType, this.stacker.NodeID, SysCfg.EnumTaskStatus.已完成.ToString(), SysCfg.EnumTaskStatus.任务撤销.ToString());
        //    DataSet ds = ctlTaskBll.GetList(strWhere);
        //    if(ds !=null && ds.Tables.Count>0&& ds.Tables[0].Rows.Count>0)
        //    {
        //        return true;
        //    }
        //    else
        //    {
        //        return false;
        //    }
        //}
        //private string SimModuleGenerate()
        //{
        //    string batchName = SysCfg.SysCfgModel.CheckinBatchHouseA;

        //    //zwx,此处需要修改
        //    //if (this.houseName == EnumStoreHouse.B1库房.ToString())
        //    //{
        //    //    batchName = SysCfg.SysCfgModel.CheckinBatchHouseB;
        //    //}
        //    //if (batchName == "空")
        //    //{
        //    //    batchName = string.Empty;
        //    //}
        //    string palletID = System.Guid.NewGuid().ToString();
        //    //for(int i=0;i<2;i++)
        //    //{
        //    //    string modID = System.Guid.NewGuid().ToString();
        //    //    CtlDBAccess.Model.BatteryModuleModel batModule = new  CtlDBAccess.Model.BatteryModuleModel();
        //    //    batModule.asmTime = System.DateTime.Now;
        //    //    batModule.batModuleID = modID;
        //    //    batModule.curProcessStage = SysCfg.EnumModProcessStage.模组焊接下盖.ToString();
        //    //    batModule.topcapOPWorkerID = "W0001";
        //    //    batModule.palletBinded = true;
        //    //    batModule.palletID = palletID;
        //    //    batModule.batchName = batchName;
        //    //    batModuleBll.Add(batModule);
        //    //}
        //    return palletID;
        //}
        /// <summary>
        /// 处理任务完成信息，更新货位状态
        /// </summary>
        /// <param name="ctlTask"></param>
        /// <returns></returns>
        private bool TaskCompletedProcess(AsrsTaskParamModel taskParamModel, ControlTaskModel ctlTask)
        {
            try
            {
                string reStr = "";
                switch (ctlTask.TaskType)
                {
                    case (int)SysCfg.EnumAsrsTaskType.产品入库:
                        {
                            if (InHouseTaskCptProcess(taskParamModel, ref reStr) == false)
                            {
                                return false;
                            }

                            string gsName = taskParamModel.CellPos1.Row + "-" + taskParamModel.CellPos1.Col + "-" + taskParamModel.CellPos1.Layer;
                            //MesDBAccess.Model.ProductOnlineModel processData = productOnlineBll.GetModelByProcessStepID("PS-1");//投产绑定时的数据

                            string processStepID = taskParamModel.MESStep;

                            EnumLogicArea logicArea = EnumLogicArea.测试区;
                            this.asrsResManage.GetLogicAreaName(this.houseName, taskParamModel.CellPos1, ref logicArea);
                            //入库后需要更新新威尔中间数据表及上报德赛MES
                            string rfid = taskParamModel.InputCellGoods[0];
                            List<MesDBAccess.Model.ProductOnlineModel> productList = null;
                            if (this.houseName == EnumStoreHouse.A1库房.ToString())
                            {
                               productList= this.productOnlineBll.GetModelList(string.Format("palletID='{0}' and palletBinded=1 and productCata='电芯' order by batchName asc", rfid));

                            }
                            else
                            {
                                productList = this.productOnlineBll.GetModelList(string.Format("palletID='{0}' and palletBinded=1 and productCata='模组' order by batchName asc", rfid));

                            }
                            string[] codeList = new string[productList.Count];
                            for (int i = 0; i < productList.Count; i++)
                            {
                                codeList[i] = productList[i].productID;
                            }
                            if (logicArea == EnumLogicArea.测试区)
                            {
                                if (this.houseName == EnumStoreHouse.A1库房.ToString())
                                {
                                    processStepID = "PS-3";
                                }
                                else
                                {
                                    processStepID = "PS-9";
                                }
                                this.XweProcessModel.InHouseTestAreaLogic(this.houseName, gsName, taskParamModel.InputCellGoods[0], codeList, ref reStr);
                            }

                            UpdateOnlineProductInfo(rfid, processStepID, taskParamModel.CellPos1.Row.ToString(),
                                taskParamModel.CellPos1.Col.ToString(), taskParamModel.CellPos1.Layer.ToString());

                            AddProduceRecord(rfid, processStepID);

                            //调用德赛接口MES BEGIN
                            if (mesenable != 0)
                            {
                                int type = 1;
                                if (logicArea == EnumLogicArea.测试区)
                                {
                                    type = 2;
                                }
                                if (this.houseName == EnumStoreHouse.A1库房.ToString())
                                {
                                    MESWCFManage.Inst().UpLoadHWA(rfid, gsName, type);
                                }
                                else if (this.houseName == EnumStoreHouse.B1库房.ToString())
                                {
                                    MESWCFManage.Inst().UpLoadHWB(rfid, gsName, type);
                                }
                            }
                            //调用德赛接口MES END
                            break;
                        }
                    case (int)SysCfg.EnumAsrsTaskType.空框入库:
                        {
                            //1 先更新货位存储状态
                            if (!this.asrsResManage.UpdateCellStatus(this.houseName, taskParamModel.CellPos1,
                                EnumCellStatus.空料框,
                                EnumGSTaskStatus.完成,
                                ref reStr))
                            {
                                logRecorder.AddLog(new LogInterface.LogModel(nodeName, "更新货位状态失败：" + reStr, LogInterface.EnumLoglevel.错误));

                                return false;
                            }

                            //2 更新库存状态
                            this.asrsResManage.AddEmptyMeterialBox(this.houseName, taskParamModel.CellPos1, ref reStr);

                            //3 更新出入库操作状态
                            this.asrsResManage.UpdateGSOper(this.houseName, taskParamModel.CellPos1, EnumGSOperate.无, ref reStr);

                            //4 增加出入库操作记录
                            this.asrsResManage.AddGSOperRecord(this.houseName, taskParamModel.CellPos1, EnumGSOperateType.入库, "", ref reStr);
                            break;
                        }
                    case (int)SysCfg.EnumAsrsTaskType.DCR测试:
                        {
                            //logRecorder.AddDebugLog(nodeName, "31");
                            if (OutHouseTaskCptProcess(ctlTask, taskParamModel, ref  reStr) == false)
                            {
                                return false;
                            }
                            //logRecorder.AddDebugLog(nodeName, "32");
                            string powerGsm = taskParamModel.CellPos1.Row + "-" + taskParamModel.CellPos1.Col + "-" + taskParamModel.CellPos1.Layer;
                            //需要更新新威尔中间数据库，开始DCR检测
                            string dcrGsm = taskParamModel.CellPos2.Row + "-" + taskParamModel.CellPos2.Col + "-" + taskParamModel.CellPos2.Layer;
                            string rfid = taskParamModel.InputCellGoods[0];
                            //logRecorder.AddDebugLog(nodeName, "32");
                            //需要上报德赛MES START
                            if (mesenable != 0)
                            {
                                if (taskParamModel.InputCellGoods == null)
                                {
                                    this.LogRecorder.AddDebugLog("DCR测试", "工装板号码为空！");
                                    break;
                                }
                                if (this.houseName == EnumStoreHouse.A1库房.ToString())
                                {
                                    MESWCFManage.Inst().UpLoadHWA(rfid, dcrGsm, 3);
                                }
                                else if (this.houseName == EnumStoreHouse.B1库房.ToString())
                                {
                                    MESWCFManage.Inst().UpLoadHWB(rfid, dcrGsm, 3);
                                }
                            }
                            //需要上报德赛MES
                            //logRecorder.AddDebugLog(nodeName, "34");
                            this.XweProcessModel.DCROutHouseCpt(this.houseName, powerGsm, dcrGsm, rfid);
                            //logRecorder.AddDebugLog(nodeName, "35");
                            break;
                        }
                    case (int)SysCfg.EnumAsrsTaskType.紧急出库:
                        {
                            if (OutHouseTaskCptProcess(ctlTask, taskParamModel, ref  reStr) == false)
                            {
                                return false;
                            }
                            string gsName = taskParamModel.CellPos1.Row + "-" + taskParamModel.CellPos1.Col + "-" + taskParamModel.CellPos1.Layer;

                            this.XweProcessModel.EmerOutHouseCmpLogic(this.houseName, gsName);
                            break;
                        }
                    case (int)SysCfg.EnumAsrsTaskType.空框出库:
                        {
                            if (OutHouseTaskCptProcess(ctlTask, taskParamModel, ref  reStr) == false)
                            {
                                return false;
                            }
                            break;
                        }
                    case (int)SysCfg.EnumAsrsTaskType.DCR出库://DCR测试完成
                        {
                            //logRecorder.AddDebugLog(nodeName, "11");
                            logRecorder.AddLog(new LogInterface.LogModel(nodeName, SysCfg.EnumAsrsTaskType.DCR出库.ToString(), LogInterface.EnumLoglevel.提示));
                            string gsName = taskParamModel.CellPos1.Row + "-" + taskParamModel.CellPos1.Col + "-" + taskParamModel.CellPos1.Layer;
                            //logRecorder.AddDebugLog(nodeName, "12");
                            //DCR测试完成
                            this.XweProcessModel.DCRTestCptLogic(this.houseName);
                            //logRecorder.AddDebugLog(nodeName, "13");
                            if (taskParamModel.InputCellGoods!=null && taskParamModel.InputCellGoods.Count()>0)
                            {
                                UpdateOnlineProductInfo(taskParamModel.InputCellGoods[0].Trim(), taskParamModel.MESStep, string.Empty, string.Empty, string.Empty);
                            
                            }
                            //logRecorder.AddDebugLog(nodeName, "14");
                            if (taskParamModel.InputCellGoods != null && taskParamModel.InputCellGoods.Count() > 0)
                            {

                                AddProduceRecord(taskParamModel.InputCellGoods[0].Trim(), taskParamModel.MESStep);
                            }
                            //logRecorder.AddDebugLog(nodeName, "15");
                            break;
                        }
                    case (int)SysCfg.EnumAsrsTaskType.移库:
                        {
                            if (MoveHouseTaskCptProcess(ctlTask, taskParamModel, ref reStr) == false)
                            {
                                return false;
                            }
                            List<string> codeList = new List<string>();

                            string plletID = "";
                            if(taskParamModel.InputCellGoods != null)
                            {
                                plletID = taskParamModel.InputCellGoods[0];
                            }

                            if (SysCfg.SysCfgModel.UnbindMode == false)//有数据绑定的时候进去下面
                            {
                                if(plletID.Length != 0)
                                {
                                    List<MesDBAccess.Model.ProductOnlineModel> productList = this.productOnlineBll.GetModelList(string.Format("palletID='{0}' and palletBinded=1 ", plletID));
                                    if (productList != null && productList.Count > 0)
                                    {
                                        for (int i = 0; i < productList.Count; i++)
                                        {
                                            codeList.Add( productList[i].productID);
                                        }
                                    }
                                    else
                                    {
                                        this.logRecorder.AddDebugLog("移库完成处理", "从在线数据中获取电芯条码数据失败！");
                                    }
                                }
                                else
                                {
                                    this.logRecorder.AddDebugLog("移库完成处理", "从在线数据中获取电芯条码数据失败！");
                                }
                            }

                            

                            string gsName = taskParamModel.CellPos2.Row + "-" + taskParamModel.CellPos2.Col + "-" + taskParamModel.CellPos2.Layer;

                            //从暂存区至测试区的移库也需要更新新威尔中间数据库
                            this.XweProcessModel.MoveHouseCptLogic(this.houseName, gsName, plletID, codeList.ToArray(), ref reStr);


                            //移库 需要上报德赛MES START
                            if (mesenable != 0)
                            {
                                if (taskParamModel.InputCellGoods == null)
                                {
                                    this.LogRecorder.AddDebugLog("移库", "工装板号码为空！");
                                    break;
                                }

                                EnumLogicArea logicArea = EnumLogicArea.测试区;
                                this.asrsResManage.GetLogicAreaName(this.houseName, taskParamModel.CellPos2, ref logicArea);
                                int type = 1;
                                if (logicArea == EnumLogicArea.测试区)
                                {
                                    type = 2;
                                }

                                string rfid = taskParamModel.InputCellGoods[0];
                                if (this.houseName == EnumStoreHouse.A1库房.ToString())
                                {
                                    MESWCFManage.Inst().UpLoadHWA(rfid, gsName, type);
                                }
                                else if (this.houseName == EnumStoreHouse.B1库房.ToString())
                                {
                                    MESWCFManage.Inst().UpLoadHWB(rfid, gsName, type);
                                }
                            }
                            //需要上报德赛MES END
                            break;
                        }
                    default:
                        break;
                }
                ctlTask.FinishTime = System.DateTime.Now;
                ctlTask.TaskStatus = SysCfg.EnumTaskStatus.已完成.ToString();
                return ctlTaskBll.Update(ctlTask);
            }
            catch (Exception ex)
            {
                logRecorder.AddLog(new LogInterface.LogModel(nodeName, "任务完成处理异常，" + ex.StackTrace, LogInterface.EnumLoglevel.错误));

                return false;
            }
        }
        private bool MoveHouseTaskCptProcess(ControlTaskModel ctlTask,  AsrsTaskParamModel taskParamModel,ref string reStr)
        {  //1 货位1的处理
            if (!this.asrsResManage.UpdateCellStatus(this.houseName, taskParamModel.CellPos1,
                EnumCellStatus.空闲,
                EnumGSTaskStatus.完成,
                ref reStr))
            {
                logRecorder.AddLog(new LogInterface.LogModel(nodeName, "更新货位状态失败：" + reStr, LogInterface.EnumLoglevel.错误));

                return false;
            }
            //this.asrsResManage.RemoveStack(this.houseName, taskParamModel.CellPos1, ref reStr);
            this.asrsResManage.UpdateGSOper(this.houseName, taskParamModel.CellPos1, EnumGSOperate.无, ref reStr);

            //增加出入库操作记录
            EnumGSOperateType gsOPType = EnumGSOperateType.系统自动出库;
            if (ctlTask.CreateMode == "手动")
            {
                gsOPType = EnumGSOperateType.手动出库;
            }

            this.asrsResManage.AddGSOperRecord(this.houseName, taskParamModel.CellPos1, gsOPType, "", ref reStr);

            //货位2的处理
            if (!this.asrsResManage.UpdateCellStatus(this.houseName, taskParamModel.CellPos2,
               EnumCellStatus.满位,
               EnumGSTaskStatus.完成,
               ref reStr))
            {
                logRecorder.AddLog(new LogInterface.LogModel(nodeName, "更新货位状态失败：" + reStr, LogInterface.EnumLoglevel.错误));
                return false;
            }
            this.asrsResManage.UpdateGSOper(this.houseName, taskParamModel.CellPos2, EnumGSOperate.无, ref reStr);
            //增加出入库操作记录
            this.asrsResManage.AddGSOperRecord(this.houseName, taskParamModel.CellPos2, EnumGSOperateType.入库, "", ref reStr);

            string batchName = string.Empty;
            //zwx,此处需要修改
            //  CtlDBAccess.BLL.BatteryModuleBll batModuleBll = new CtlDBAccess.BLL.BatteryModuleBll();
            if (taskParamModel.InputCellGoods != null && taskParamModel.InputCellGoods.Count() > 0)
            {
                string palletID = taskParamModel.InputCellGoods[0];
                batchName = productOnlineBll.GetBatchNameofPallet(palletID);
                // CtlDBAccess.Model.BatteryModuleModel batModule = batModuleBll.GetModel(taskParamModel.InputCellGoods[0]);
                // batchName = batModule.batchName;
            }

            this.asrsResManage.RemoveStack(houseName, taskParamModel.CellPos1, ref reStr);
            if (taskParamModel.InputCellGoods != null && taskParamModel.InputCellGoods.Count() > 0)
            {
                for (int i = 0; i < taskParamModel.InputCellGoods.Count(); i++)
                {
                    UpdateOnlineProductInfo(taskParamModel.InputCellGoods[i], taskParamModel.MESStep,
                        taskParamModel.CellPos2.Row.ToString(), taskParamModel.CellPos2.Col.ToString()
                        , taskParamModel.CellPos2.Layer.ToString());

                    AddProduceRecord(taskParamModel.InputCellGoods[i], taskParamModel.MESStep);
                }
                if (!this.asrsResManage.AddStack(houseName, taskParamModel.CellPos2, batchName, taskParamModel.InputCellGoods, ref reStr))
                {
                    logRecorder.AddDebugLog(nodeName, string.Format("货位:{0}-{1}-{2}增加库存信息失败，{3}", taskParamModel.CellPos2.Row, taskParamModel.CellPos2.Col, taskParamModel.CellPos2.Layer, reStr));

                }
            }
                           
            return true;
        }
        private bool InHouseTaskCptProcess( AsrsTaskParamModel taskParamModel, ref string reStr)
        {
         
            //1 先更新货位存储状态
            if (!this.asrsResManage.UpdateCellStatus(this.houseName, taskParamModel.CellPos1,
                EnumCellStatus.满位,
                EnumGSTaskStatus.完成,
                ref reStr))
            {
                logRecorder.AddLog(new LogInterface.LogModel(nodeName, "更新货位状态失败：" + reStr, LogInterface.EnumLoglevel.错误));

                return false;
            }
             
            //2 更新库存状态
            //获取入库批次，临时调试用
            //string batchName = SysCfgModel.CheckinBatchHouseA;
            //if(this.houseName == EnumStoreHouse.B库房.ToString())
            //{
            //    batchName = SysCfgModel.CheckinBatchHouseB;
            //}
            string batchName = string.Empty;

            //zwx,此处需要修改
            //  CtlDBAccess.BLL.BatteryModuleBll batModuleBll = new CtlDBAccess.BLL.BatteryModuleBll();

            if (SysCfg.SysCfgModel.UnbindMode)
            {
                batchName = SysCfg.SysCfgModel.CheckinBatchDic[houseName];
            }
            else
            {
               
                if (taskParamModel.InputCellGoods != null && taskParamModel.InputCellGoods.Count() > 0)
                {
                    string palletID = taskParamModel.InputCellGoods[0];
                    batchName = productOnlineBll.GetBatchNameofPallet(palletID);
                    // CtlDBAccess.Model.BatteryModuleModel batModule = batModuleBll.GetModel(taskParamModel.InputCellGoods[0]);
                    // batchName = batModule.batchName;
                }
            }
          
            this.asrsResManage.AddStack(this.houseName, taskParamModel.CellPos1, batchName, taskParamModel.InputCellGoods, ref reStr);
            
            //3 更新出入库操作状态
            this.asrsResManage.UpdateGSOper(this.houseName, taskParamModel.CellPos1, EnumGSOperate.无, ref reStr);
            
            //4 增加出入库操作记录
            
            this.asrsResManage.AddGSOperRecord(this.houseName, taskParamModel.CellPos1, EnumGSOperateType.入库, "", ref reStr);
           
            
            for (int i = 0; i < taskParamModel.InputCellGoods.Count(); i++)
            {

                //UpdateOnlineProductInfo(taskParamModel.InputCellGoods[i], taskParamModel.MESStep);
                UpdateOnlineProductInfo(taskParamModel.InputCellGoods[i], taskParamModel.MESStep,taskParamModel.CellPos1.Row.ToString(),
                    taskParamModel.CellPos1.Col.ToString(),taskParamModel.CellPos1.Layer.ToString());

                AddProduceRecord(taskParamModel.InputCellGoods[i], taskParamModel.MESStep);
            }

            return true;
        }

        private bool OutHouseTaskCptProcess(ControlTaskModel ctlTask,  AsrsTaskParamModel taskParamModel,ref string reStr)
        {

            if (!this.asrsResManage.UpdateCellStatus(this.houseName, taskParamModel.CellPos1,
                              EnumCellStatus.空闲,
                              EnumGSTaskStatus.完成,
                              ref reStr))
            {
                logRecorder.AddLog(new LogInterface.LogModel(nodeName, "更新货位状态失败：" + reStr, LogInterface.EnumLoglevel.错误));

                return false;
            }
            //2 移除库存
            this.asrsResManage.RemoveStack(this.houseName, taskParamModel.CellPos1, ref reStr);

            //3 更新出入库操作状态
            this.asrsResManage.UpdateGSOper(this.houseName, taskParamModel.CellPos1, EnumGSOperate.无, ref reStr);

            //4 增加出入库操作记录
            EnumGSOperateType gsOPType = EnumGSOperateType.系统自动出库;
            if (ctlTask.CreateMode == "手动")
            {
                gsOPType = EnumGSOperateType.手动出库;
            }
            this.asrsResManage.AddGSOperRecord(this.houseName, taskParamModel.CellPos1, gsOPType, "", ref reStr);
            for (int i = 0; taskParamModel.InputCellGoods != null && i < taskParamModel.InputCellGoods.Count(); i++)
            {
                UpdateOnlineProductInfo(taskParamModel.InputCellGoods[i], taskParamModel.MESStep, taskParamModel.CellPos2.Row.ToString()
                    , taskParamModel.CellPos2.Col.ToString(), taskParamModel.CellPos2.Layer.ToString());

                AddProduceRecord(taskParamModel.InputCellGoods[i], taskParamModel.MESStep);
            }
            return true;
        }
        /// <summary>
        /// 根据任务，获取绑定的出口
        /// </summary>
        /// <param name="taskType"></param>
        /// <returns></returns>
        private List<AsrsPortalModel> GetOutPortsOfBindedtask(SysCfg.EnumAsrsTaskType taskType)
        {
            List<AsrsPortalModel> validPorts = new List<AsrsPortalModel>();
            foreach(AsrsPortalModel port in ports)
            {
                if(port.BindedTaskOutput == taskType)
                {
                    validPorts.Add(port);
                }
            }
            //if(this.houseName== EnumStoreHouse.A1库房.ToString()) //特殊处理，A1库产品空框混流出库
            //{
            //    validPorts.Add(ports[1]);
            //}
            return validPorts;
        }
        
        #endregion
    }
}
