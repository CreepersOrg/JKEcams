using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Data;
using DevAccess;
using FlowCtlBaseModel;
using DevInterface;
using CtlDBAccess.Model;
using CtlDBAccess.BLL;
using System.Threading;

using SysCfg;
using WCFClient;
using System.Configuration;


namespace ProcessCtl
{
    /// <summary>
    /// 人工模组绑定
    /// </summary>
    public class NodeManualBind : CtlNodeBaseModel
    {
        private int snSize = 10; // 人工绑定电池码的个数
        private string StartBarcode = ConfigurationManager.AppSettings["StartBarcode"].ToLower(); //扫码时必须要扫的第一个码

        private int barcodeSize = 15;
        private int barcodeTPSize = 6;
        int mesenable = Convert.ToInt32(ConfigurationManager.AppSettings["MESEnable"]);


        public int SNSize { get { return snSize; } }
        public int BarcodeSize { get { return barcodeSize; } }
        public int BarcodeTPSize { get { return barcodeTPSize; } }


        private string tpCode = string.Empty;

        public NodeManualBind()
        {

        }
        public override bool BuildCfg(System.Xml.Linq.XElement xe, ref string reStr)
        {
            if (!base.BuildCfg(xe, ref reStr))
            {
                return false;
            }
            XElement selfDataXE = xe.Element("SelfDatainfo");
            if (selfDataXE != null)
            {

                if (selfDataXE.Attribute("snSize") != null)
                {
                    this.snSize = int.Parse(selfDataXE.Attribute("snSize").Value.ToString());
                }
                if (selfDataXE.Attribute("barcodeSize") != null)
                {
                    this.barcodeSize = int.Parse(selfDataXE.Attribute("barcodeSize").Value.ToString());
                }
                if (selfDataXE.Attribute("barcodeTPSize") != null)
                {
                    this.barcodeTPSize = int.Parse(selfDataXE.Attribute("barcodeTPSize").Value.ToString());
                }
            }
            this.dicCommuDataDB1[1].DataDescription = "第一组模组 1：复位,2：流程已开始,3：正常完成（3秒自复位）,4：异常完成（3秒自复位）";
            this.dicCommuDataDB1[2].DataDescription = "第二组模组 1：复位,2：流程已开始,3：正常完成（3秒自复位）,4：异常完成（3秒自复位）";

            this.dicCommuDataDB2[1].DataDescription = "第一组模组 1：复位,2：装载完成（正常或异常完成时复位）";
            this.dicCommuDataDB2[2].DataDescription = "第二组模组 1：复位,2：装载完成（正常或异常完成时复位）";
            currentTaskPhase = 0;

            return true;
        }

        //EnumAsrsTaskType GetTaskTypeByNodeName(string nodeName)
        //{
        //    if (nodeName == "扫码工位1")
        //    {
        //        return EnumAsrsTaskType.扫码工位1;
        //    }
        //    else if (nodeName == "扫码工位2")
        //    {
        //        return EnumAsrsTaskType.扫码工位2;
        //    }
        //    else if (nodeName == "扫码工位3")
        //    {
        //        return EnumAsrsTaskType.扫码工位3;
        //    }
        //    else
        //    {
        //        return EnumAsrsTaskType.空;
        //    }
        //}

        public void SetVal1(object obj)
        {
            string retStr = string.Empty;
            Thread.Sleep(3000);
            this.db1ValsToSnd[0] = 1;
            NodeCmdCommit(false,ref retStr);
        }

        public void SetVal2(object obj)
        {
            string retStr = string.Empty;
            Thread.Sleep(3000);
            this.db1ValsToSnd[1] = 1;
            NodeCmdCommit(false, ref retStr);
        }

        public override bool ExeBusiness(ref string reStr)
        {
            if (!nodeEnabled)
            {
                return true;
            }
            // 查看此节点是否有任务在执行或排队
            if (!devStatusRestore)
            {
                devStatusRestore = DevStatusRestore();
            }
            if (!devStatusRestore)
            {
                return false;
            }
            else 
            {
                if (this.currentTask == null)
                {
                    this.db1ValsToSnd[0] = 1;
                    this.db1ValsToSnd[1] = 1;
                }
            }

            if (!FillTaskRequire(ref reStr))
            {
                return false;
            }

            if (this.currentTask == null)
            {
                this.db1ValsToSnd[0] = 1;
                this.db1ValsToSnd[1] = 1;
                return true;
            }

            switch (this.currentTaskPhase)
            {
                case 1:
                    {
                        currentTaskDescribe = "开始执行扫描任务";
                        this.db1ValsToSnd[0] = 2;
                        this.currentTaskPhase++;
                        this.currentTask.TaskPhase = this.currentTaskPhase;
                        this.ctlTaskBll.Update(this.currentTask);
                        logRecorder.AddDebugLog(nodeName, string.Format("等待第一组扫描完成"));
                        break;
                    }
                case 2:
                    {
                        if (db2Vals[0] != 2)
                        {
                            break;
                        }

                        if (db1ValsToSnd[0] != 4)
                        {
                            logRecorder.AddDebugLog(this.nodeName, "第一组开始验证");
                        }
                        List<string> barcodes = new List<string>();
                        // 获取读取到的BarCode列表
                        if (SysCfg.SysCfgModel.SimMode)
                        {
                           // GenerateSimBarCode(ref barcodes);
                            if(this.SimBarcode.Length == 0)
                            {
                                break;
                            }

                            string[] simBarcodes = SimBarcode.Split(',');

                            for (int i = 0; i < simBarcodes.Length; i++)
                            {
                                barcodes.Add(simBarcodes[i]);
                            }
                        }
                        else
                        {
                            barcodes = barcodeRW.GetBarcodesBuf();//
                        }
                        //验证BarCode的格式START
                        if (!VerifyBarcode(ref barcodes,1))
                        {
                            if (db1ValsToSnd[0] != 4)
                            {
                                this.logRecorder.AddDebugLog(this.nodeName, "第一组验证失败");
                            }
                            db1ValsToSnd[0] = 4;
                            this.currentTaskPhase = 0;
                            ThreadPool.QueueUserWorkItem(new WaitCallback(SetVal1));
                            return true;
                        }
                        logRecorder.AddDebugLog(this.nodeName, "第一组验证成功");
                        //验证BarCode的格式END

                        string hmCode = string.Empty;
                        string hkCode = string.Empty;

                        // 上传到MES系统START
                        ReportBarcode(ref barcodes);
                        // 上传到MES系统END


                        this.db1ValsToSnd[0] = 3;

                        ThreadPool.QueueUserWorkItem(new WaitCallback(SetVal1));

                        this.currentTaskPhase++;
                        this.currentTask.TaskPhase = this.currentTaskPhase;

                        string dbtag = string.Empty;
                        foreach(string str in barcodes)
                        {
                            dbtag += str + ",";
                        }

                        dbtag = dbtag.Substring(0,dbtag.Length - 1);

                        this.currentTask.tag1 = dbtag;

                        this.ctlTaskBll.Update(this.currentTask);
                        logRecorder.AddDebugLog(nodeName, "等待第二组扫码.......");
                        break;
                    }
                case 3:
                    {
                        currentTaskDescribe = "等待第二组扫码.......";
                        if (db2Vals[1] != 2)
                        {
                            break;
                        }
                        if (db1ValsToSnd[1] != 4)
                        {
                            logRecorder.AddDebugLog(this.nodeName, "第二组开始验证");
                        }
                        List<string> barcodes = new List<string>();
                        // 获取读取到的BarCode列表
                        if (SysCfg.SysCfgModel.SimMode)
                        {
                            //GenerateSimBarCode(ref barcodes);

                            if (this.SimBarcode.Length == 0)
                            {
                                break;
                            }

                            string[] simBarcodes = SimBarcode.Split(',');

                            for (int i = 0; i < simBarcodes.Length; i++)
                            {
                                barcodes.Add(simBarcodes[i]);
                            }
                        }
                        else
                        {
                            barcodes = barcodeRW.GetBarcodesBuf();//
                        }
                        //验证BarCode的格式START
                        if (!VerifyBarcode(ref barcodes,2))
                        {
                            if (db1ValsToSnd[1] != 4)
                            {
                                logRecorder.AddDebugLog(this.nodeName, "第二组验证失败");
                            }
                            db1ValsToSnd[1] = 4;
                            ThreadPool.QueueUserWorkItem(new WaitCallback(SetVal2));

                            return true;
                        }
                        //验证BarCode的格式END

                        // 上传到MES系统START
                        ReportBarcode(ref barcodes);
                        // 上传到MES系统END

                        logRecorder.AddDebugLog(this.nodeName, "第二组验证成功");

                        string dbtag = string.Empty;
                        foreach (string str in barcodes)
                        {
                            dbtag += str + ",";
                        }

                        dbtag = dbtag.Substring(0, dbtag.Length - 1);

                        this.currentTask.tag2 = dbtag;

                        this.currentTaskPhase++;
                        this.currentTask.TaskPhase = this.currentTaskPhase;
                        this.ctlTaskBll.Update(this.currentTask);
                        this.db1ValsToSnd[1] = 3;
                        ThreadPool.QueueUserWorkItem(new WaitCallback(SetVal2));
                        break;
                    }
                case 4:
                    {
                        if ((this.db2Vals[0] != 1) || (this.db2Vals[1] != 1))
                        {
                            break;
                        }
                        logRecorder.AddDebugLog(nodeName, "更新数据库成功,扫描完成");
                        this.currentTask.FinishTime = System.DateTime.Now;
                        this.currentTask.TaskStatus = SysCfg.EnumTaskStatus.已完成.ToString();
                        this.ctlTaskBll.Update(this.currentTask);
                        this.currentTask = null;
                        currentTaskPhase = 0;
                        logRecorder.AddDebugLog(nodeName, "等待执行下一个任务");
                        currentTaskDescribe = "等待执行下一个任务";
                        break;
                    }
                default:
                    break;
            }
            return true;
        }

       void  GenerateSimBarCode(ref List<string> barcodes)
       {
           barcodes.Add(StartBarcode);
           for (int i = 0; i < snSize + 2; i++)
           {
               string barcodeID = string.Format("BarCode-{0}-{1}", (i + 1).ToString().PadLeft(6, '0'),System.DateTime.Now.ToLongTimeString());
               barcodes.Add(barcodeID);
           }
       }


         // TODO:查找工位号码的 字符串
       public bool ListFind1(string name)
       {
           if (name.Contains("TP2"))
           {
               return true;
           }
           return false;
       }

       public bool ListFind2(string name)
       {
           if (name.Contains("HSM"))
           {
               return true;
           }

           return false;
       }

       public bool ListFind3(string name)
       {
           if (name.Contains("HSK"))
           {
               return true;
           }

           return false;
       }

       public bool WithStart(String s)
       {
           if ( s.ToLower().Trim() == StartBarcode)
           {
               return true;
           }
           else
           {
               return false;
           }
       }


       bool InsertToDB(string[] batteryList,string rfid,string hmCode)
       {
           int batteryIndex = 1;
           int gourpBatteryIndex = 1;
           for (int i = 0; i < batteryList.Length + 1; i++)
           {
               string batteryID = string.Empty;
               if( i < batteryList.Length)
               {
                   batteryID = batteryList[i];
               }
               else
               {
                   batteryID = hmCode;
               }

               MesDBAccess.Model.ProductOnlineModel productModel = null;
               if (productOnlineBll.Exists(batteryID))
               {
                   productModel = productOnlineBll.GetModel(batteryID);
                   productModel.productID = batteryID;
                   productModel.palletID = rfid;
                   productModel.modifyTime = System.DateTime.Now;
                   productModel.processStepID = this.mesProcessStepID[0];
                   if (i < batteryList.Length)
                   {
                       productModel.batchName = batteryIndex.ToString();
                       batteryIndex++;
                       productModel.productCata = SysCfg.EnumProductCata.电芯.ToString();
                      
                   }
                   else
                   {
                       productModel.batchName = gourpBatteryIndex.ToString();
                       gourpBatteryIndex++;
                       productModel.productCata = SysCfg.EnumProductCata.模组.ToString();
                   }
                   
                   productModel.palletBinded = true;
                   productModel.stationID = this.nodeID;
                   productModel.checkResult = "0";
                   if (!productOnlineBll.Update(productModel))
                   {
                       return false;
                   }
               }
               else
               {
                   productModel = new MesDBAccess.Model.ProductOnlineModel();
                   productModel.onlineTime = System.DateTime.Now;
                   productModel.modifyTime = System.DateTime.Now;
                   productModel.productID = batteryID;
                   productModel.palletID = rfid;
                   productModel.processStepID = this.mesProcessStepID[0];
                   if (i < batteryList.Length)
                   {
                       productModel.batchName = batteryIndex.ToString();
                       batteryIndex++;
                       productModel.productCata = SysCfg.EnumProductCata.电芯.ToString();
                   }
                   else
                   {
                       productModel.batchName = gourpBatteryIndex.ToString();
                       gourpBatteryIndex++;
                       productModel.productCata = SysCfg.EnumProductCata.模组.ToString();
                   }
                   productModel.palletBinded = true;
                   productModel.stationID = this.nodeID;
                   productModel.checkResult = "0";
                   
                   if (!productOnlineBll.Add(productModel))
                   {
                       return false;
                   }
               }
           }
           return true;
       }

        bool VerifyBarcode(ref List<string> barcodes,int type)
        {
            // 找到最后一个StartBarcode 
            int index = 0;
            int startIndex = 0;
            foreach (string singleBarcode in barcodes)
            {
                if (singleBarcode.ToLower() == StartBarcode)
                {
                    startIndex = index;
                }
                else
                {
                    index++;
                }
            }

            // 将在查找到的Barcode之前的都删除 
            if(startIndex != 0)
            {
                barcodes.RemoveRange(0, (startIndex + 1));
            }
            barcodes.RemoveAll(WithStart);
            List<string> recvBarcodesBuf = new List<string>();

            foreach(string str in barcodes)
            {
                if (!recvBarcodesBuf.Contains(str))
                {
                    recvBarcodesBuf.Add(str);
                }
            }


            // 判断个数是否正确
            if (recvBarcodesBuf.Count != this.snSize + 2)
            {
                this.logRecorder.AddDebugLog(this.nodeName, "错误:扫码的数量不正确，扫描到 " + barcodes.Count.ToString());
                return false;
            }

            // 判断是否含有工位工号
            List<string> subList = recvBarcodesBuf.FindAll(ListFind1); 
            if(subList.Count != 1)
            {
                this.logRecorder.AddDebugLog(this.nodeName, "错误:扫码中没包含工位码" );
                return false;
            }
            else
            {
               if(subList[0].Length != barcodeTPSize + 3)
               {
                   this.logRecorder.AddDebugLog(this.nodeName, "错误:扫码中工位码的长度不符！");
                   return false;
               }
            }
            string rfid = subList[0].Trim();

            //
            string hmCode = string.Empty;
            subList = recvBarcodesBuf.FindAll(ListFind2);
            if (subList.Count != 1)
            {
                this.logRecorder.AddDebugLog(this.nodeName, "错误:扫码中没包含模组码");
                return false;
            }
            else
            {
                if (subList[0].Length != barcodeSize)
                {
                    this.logRecorder.AddDebugLog(this.nodeName, "错误:扫码中模组码的长度不符！");
                    return false;
                }
            }
            hmCode = subList[0];

            subList = recvBarcodesBuf.FindAll(ListFind3);
            if (subList.Count != snSize)
            {

                this.logRecorder.AddDebugLog(this.nodeName, "错误:扫描的模块数量不正确，模块数量:" + subList.Count.ToString());

                return false;
            }
            else 
            {
                for(int i = 0;i < subList.Count;i++)
                {
                    if (subList[i].Length != barcodeSize)
                    {
                        this.logRecorder.AddDebugLog(this.nodeName, "错误:扫描的模块码长度不符！模块码:" + subList[i].ToString());
                        return false;
                    }
                }
            }


            barcodes.Clear();
            foreach (string str in recvBarcodesBuf)
            {
                barcodes.Add(str);
            }

            if(type == 1)
            {
                tpCode = rfid;
            }
            else if(type == 2)
            {
                if(tpCode != rfid)
                {
                    this.logRecorder.AddDebugLog(this.nodeName, "错误:第二次扫描的工装板和第一次不相同！第一次工装板:" + tpCode + " 第二次:" + rfid);
                    return false;
                }
            }

            InsertToDB(subList.ToArray(), rfid,hmCode);
            AddProduceRecord(rfid, this.mesProcessStepID[0]);
            return true;
        }


        bool ReportBarcode(ref List<string> barcodes)
        {
            bool ret = true;
            if (mesenable != 0)
            {
                string reportStr = string.Empty;
                List<string> subList = barcodes.FindAll(ListFind1);
                reportStr = subList[0] + "=";

                subList = barcodes.FindAll(ListFind2);
                reportStr += subList[0] + "=";

                subList = barcodes.FindAll(ListFind3);

                foreach (string str in subList)
                {
                    reportStr += str + ",";
                }
                reportStr = reportStr.Substring(0, reportStr.Length - 1);
                ret = MESWCFManage.Inst().ScanBind10(reportStr);
            }
            return ret;
        }

        /// <summary>
        /// 任务申请
        /// </summary>
        private bool FillTaskRequire(ref string reStr)
        {
            EnumAsrsTaskType taskType = EnumAsrsTaskType.扫码工位;
            //if (taskType != EnumAsrsTaskType.空)
            //{
            //    if (ExistUnCompletedTask((int)taskType))
            //    {
            //        return true;
            //    }
            //}
            string getBarcode = string.Empty;
            if (((this.db1ValsToSnd[0] == 1 || this.db1ValsToSnd[0] == 0) && this.currentTask == null)
                || ((this.db1ValsToSnd[1] == 1 || this.db1ValsToSnd[1] == 0 || this.db1ValsToSnd[1] == 4) && this.currentTaskPhase == 3)
                || ((this.currentTaskPhase == 0) && (this.currentTask != null)))
            {
                if (SysCfg.SysCfgModel.SimMode)
                {
                    getBarcode = this.SimRfidUID;
                }
                else
                {
                    getBarcode = barcodeRW.ReadBarcode();// 
                }

                if (string.IsNullOrWhiteSpace(getBarcode))
                {
                    if (this.currentTask == null)
                    {
                        this.db1ValsToSnd[0] = 1;
                    }
                    return true;
                }

                // 如果读取到的的Barcode 不是开始
                if (getBarcode.ToLower() != StartBarcode)
                {
                    return false;
                }
                else
                {
                    if (this.db2Vals[0] == 2 || this.db2Vals[1] == 2)
                    {
                        this.logRecorder.AddDebugLog(nodeName, "扫描到Start条形码，DB2的状态错误！");
                        return false;
                    }
                }
                if (((this.db1ValsToSnd[0] == 1 || this.db1ValsToSnd[0] == 0) && (this.currentTask == null))
                    || ((this.currentTaskPhase == 0) && (this.currentTask != null)))
                {
                    this.logRecorder.AddDebugLog(nodeName, "第一组 已扫描Start条形码！");
                    this.db1ValsToSnd[0] = 2;

                    if (((this.currentTaskPhase == 0) && (this.currentTask != null)))
                    {
                        this.currentTaskPhase = 1;

                    }
                }

                if (((this.db1ValsToSnd[1] == 1 || this.db1ValsToSnd[1] == 0 || this.db1ValsToSnd[1] == 4) && this.currentTaskPhase == 3))
                {
                    this.logRecorder.AddDebugLog(nodeName, "第二组 已扫描Start条形码！");
                    this.db1ValsToSnd[1] = 2;
                }
            }

            if (this.currentTask == null)
            {

                //生成新任务
                this.currentTaskPhase = 1;
                ControlTaskModel task = new ControlTaskModel();
                task.DeviceID = this.nodeID;
                task.CreateMode = "自动";
                task.CreateTime = System.DateTime.Now;
                task.TaskParam = this.nodeID;
                task.TaskID = System.Guid.NewGuid().ToString("N");
                task.TaskType = (int)taskType;
                task.TaskPhase = this.currentTaskPhase;
                task.TaskStatus = SysCfg.EnumTaskStatus.执行中.ToString();
                task.Remark = nodeName.ToString();
                this.ctlTaskBll.Add(task);
                this.currentTask = task;
                currentTaskDescribe = "扫码任务生成";
            }
            return true;

        }

 

    }
}

