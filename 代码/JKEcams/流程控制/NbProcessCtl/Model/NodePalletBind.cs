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
using System.Configuration;
using WCFClient;

namespace ProcessCtl
{
    /// <summary>
    /// 电芯-托盘绑定
    /// </summary>
    public class NodePalletBind : CtlNodeBaseModel
    {
        private int snSize = 12;
        private string stockName = "";
        int mesenable = Convert.ToInt32( ConfigurationManager.AppSettings["MESEnable"]);


        public int SNSize { get { return snSize; } }

        public NodePalletBind()
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

                if (selfDataXE.Attribute("storename") != null)
                {
                    this.stockName = selfDataXE.Attribute("storename").Value.ToString();
                }
            }
            this.dicCommuDataDB1[1].DataDescription = "1:复位，2：装载处理完成，3：撤销完成,4:MES返回装载错误,5：电池为空";
            this.dicCommuDataDB1[2].DataDescription = "1：复位,2：读卡完成,放行空板到装载位置,3：读RFID失败";
            this.dicCommuDataDB2[1].DataDescription = "1：无板，2：有板";
            this.dicCommuDataDB2[2].DataDescription = "1：复位，2：装载完成,3:任务撤销";
            //for (int i = 0; i < 36 * 20;i++ )
            //{
            //    this.dicCommuDataDB2[3+i].DataDescription = "电池条码";
            //}
            currentTaskPhase = 0;

            return true;
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

            //任务撤销 
            if (db2Vals[1] == 3 )
            {
                if (this.currentTask != null && this.currentTaskPhase > 0)
                {
                    this.currentTask.TaskStatus = SysCfg.EnumTaskStatus.任务撤销.ToString();
                    this.currentTask.FinishTime = System.DateTime.Now;
                    ctlTaskBll.Update(this.currentTask);

                    this.logRecorder.AddDebugLog(this.nodeName, "装载任务撤销");
                    currentTaskDescribe = "装载任务撤销";
                    this.currentTask = null;
                    this.currentTaskPhase = 0;
                }
                //Array.Clear(this.db1ValsToSnd, 0, this.db1ValsToSnd.Count());
                this.db1ValsToSnd[0] = 3;//
                return true;
            }

            if (db2Vals[1] == 1)
            {
                db1ValsToSnd[0] = 1;
                db1ValsToSnd[1] = 1;
                NodeCmdCommit(false, ref reStr);

            }

            if (db1ValsToSnd[0] == 3 )
            {
                //任务撤销命令复位，应答也复位
                this.db1ValsToSnd[0] = 1;
                return true;

            }

            if (!FillTaskRequire(ref reStr))
            {
                return false;
            }

            if (this.currentTask == null)
            {
                return true;
            }

            switch (this.currentTaskPhase)
            {
                case 1:
                    {
                        currentTaskDescribe = "开始执行装载任务";
                        this.db1ValsToSnd[1] = 2;
                        this.currentTaskPhase++;
                        this.currentTask.TaskPhase = this.currentTaskPhase;
                        this.ctlTaskBll.Update(this.currentTask);

                        this.logRecorder.AddDebugLog(nodeName, this.rfidUID + "将RFID上报MES系统");

                        break;
                    }
                case 2:
                    {
                        currentTaskDescribe = this.rfidUID + "将RFID上报MES系统";       
 
                        if ( !SysCfg.SysCfgModel.SimMode)
                        {

                            if (mesenable != 0)
                            {
                                if (!MESWCFManage.Inst().UpLoadRID(this.rfidUID))
                                {
                                    if (this.db1ValsToSnd[0] != 4 && this.currentTaskPhase == 1)
                                    {
                                        this.logRecorder.AddDebugLog(nodeName, this.rfidUID + "装载，读到托盘号后，RFID上报MES系统失败！");
                                    }
                                    this.db1ValsToSnd[0] = 4;
                                    break;
                                }   
                            }
                        }

                        //// 将读卡请求复位START
                        //this.db1ValsToSnd[1] = 1;
                        //// 将读卡请求复位END

                        this.currentTaskPhase++;
                        this.currentTask.TaskPhase = this.currentTaskPhase;
                        this.ctlTaskBll.Update(this.currentTask);

                        this.logRecorder.AddDebugLog(nodeName,  this.rfidUID + "装载，RFID上报MES完成，等待装载完成");
                        break;
                    }
                case 3:
                    {
                        //等待扫码完成
                        currentTaskDescribe = this.rfidUID + "装载，RFID上报MES完成，等待装载完成";

                        if (db2Vals[1] != 2)
                        {
                            break;
                        }

                        //// 将读卡请求复位START
                        this.db1ValsToSnd[1] = 1;
                        //// 将读卡请求复位END

                        int ret = 0;
                        string[] singleSNCollect = new string[snSize];
                        if (SysCfg.SysCfgModel.SimMode)
                        {
                            //生成模拟数据
                            GenerateSimBatterys(ref singleSNCollect);
                        }
                        else
                        {
                            // 从MES中获取电池信息
                            ret = GetBatteryInfoFromMES(this.rfidUID, ref reStr,ref singleSNCollect);
                            if (ret != 0)
                            {
                                currentTaskDescribe = this.rfidUID + "装载错误 " + reStr;
                                if ( this.db1ValsToSnd[0] != 5 && this.db1ValsToSnd[0] != 4)
                                {
                                    this.logRecorder.AddDebugLog(nodeName, currentTaskDescribe);
                                }
                                if (ret == 1)
                                {
                                    this.db1ValsToSnd[0] = 5;
                                }
                                else if (ret == 2)
                                {
                                    this.db1ValsToSnd[0] = 4;
                                }
                               
                                break;
                            }
                        }


                        currentTaskDescribe = this.rfidUID + "装载，从MES中获取电芯信息完成";
                        this.logRecorder.AddDebugLog(nodeName, "从MES中获取电芯信息完成");

                        // 将信息放入到OnLine数据库
                        bool insertFlag = InsertToDB(singleSNCollect);
                        if (insertFlag == false)
                        {
                            this.logRecorder.AddDebugLog(nodeName, this.rfidUID + "数据库更新失败");

                            currentTaskDescribe = this.rfidUID + "装载，数据库更新失败";
                            break;
                        }

                        AddProduceRecord(this.rfidUID, this.mesProcessStepID[0]);

                        this.currentTaskPhase++;
                        this.currentTask.TaskPhase = this.currentTaskPhase;
                        this.ctlTaskBll.Update(this.currentTask);
                        this.db1ValsToSnd[0] = 2;
                        this.logRecorder.AddDebugLog(nodeName, this.rfidUID + "等待完成信号复位");
                        break;
                    }
                case 4:
                    {
                        currentTaskDescribe = this.rfidUID + "装载，等待完成信号复位";
                        if (this.db2Vals[1] != 1)
                        {
                            break;
                        }

                        currentTaskDescribe = this.rfidUID + "装载，DB1信号复位";
                        this.logRecorder.AddDebugLog(nodeName, this.rfidUID + "DB1信号复位");

                        // 需将读卡请求复位START
                        this.db1ValsToSnd[0] = 1;
                        this.db1ValsToSnd[1] = 1;
                        // 将读卡请求复位END

                        this.currentTask.FinishTime = System.DateTime.Now;
                        this.currentTask.TaskStatus = SysCfg.EnumTaskStatus.已完成.ToString();
                        this.ctlTaskBll.Update(this.currentTask);
                        this.currentTask = null;
                        currentTaskPhase = 0;
                        this.logRecorder.AddDebugLog(nodeName, "等待执行下一个任务");
                        currentTaskDescribe = "等待执行下一个任务";
                        //等待扫码完成
                        break;
                    }
                default:
                    break;
            }
            return true;
        }

        private void GenerateSimBatterys(ref string[] singleSNCollect)
        {
            // batterys = new List<string>();
            int batterySum = productOnlineBll.GetRecordCount("");
            for (int i = 0; i < snSize; i++)
            {
                batterySum++;
                string batteryID = string.Format("36ANCCB23140160n{0}16C3101{1}", "16A002", batterySum.ToString().PadLeft(6, '0'));
                //  batterys.Add(batteryID);
                singleSNCollect[i] = batteryID;

                byte[] byteArray = System.Text.ASCIIEncoding.UTF8.GetBytes(batteryID);
                Int16[] vals = new Int16[20];
                Array.Clear(vals, 0, 20);
                int blockAlloc = byteArray.Count() / 2;
                for (int blockIndex = 0; blockIndex < blockAlloc; blockIndex++)
                {
                    vals[blockIndex] = (Int16)(byteArray[blockIndex * 2] + (byteArray[blockIndex * 2 + 1] << 8));
                }
                if (blockAlloc * 2 < byteArray.Count())
                {
                    vals[blockAlloc] = byteArray[byteArray.Count() - 1];
                    blockAlloc++;
                }
                int db2St = 2 + i * 20;
                Array.Copy(vals, 0, this.db2Vals, db2St, blockAlloc);
            }
        }

        bool VerifyRFID(string readrfid)
        {
            if (SysCfg.SysCfgModel.SimMode || SysCfg.SysCfgModel.RfidSimMode)
            {
                return true;
            }
            return true;
        }
        

        /// <summary>
        /// 装载任务申请
        /// </summary>
        private bool FillTaskRequire(ref string reStr)
        {
            if (db2Vals[0] == 2 && db1ValsToSnd[0] != 2)
            {
                if (ExistUnCompletedTask((int)SysCfg.EnumAsrsTaskType.托盘装载))
                {
                    return true;
                }
                //先读RFID卡
                currentTaskDescribe = "开始读RFID";
                this.rfidUID = "";
                if (SysCfg.SysCfgModel.SimMode)
                {
                    this.rfidUID = this.SimRfidUID;
                }
                else
                {
                    this.rfidUID = rfidRW.ReadStrData();// 
                }
                this.rfidUID = this.rfidUID.TrimEnd('\0');
                this.rfidUID = this.rfidUID.Trim();

                if (string.IsNullOrWhiteSpace(this.rfidUID))
                {
                    if (this.db1ValsToSnd[1] != 3)
                    {
                        this.logRecorder.AddDebugLog(nodeName, "读RFID失败");
                    }
                    this.db1ValsToSnd[1] = 3;
                    return true;
                }
                //if (this.db1ValsToSnd[1] != 3)
                {
                    this.logRecorder.AddDebugLog(this.nodeName, "读到托盘号:" + this.rfidUID.Trim());
                }

                //验证RFID的格式START
                if (!VerifyRFID(this.rfidUID))
                {
                    if (this.db1ValsToSnd[1] != 3)
                    {
                        this.logRecorder.AddDebugLog(nodeName, string.Format("{0} RFID验证失败", this.rfidUID.Trim()));
                    }
                    this.db1ValsToSnd[1] = 3;
                    return true;
                }
                //验证RFID的格式END
                

                //解绑
                currentTaskDescribe = "开始解绑";
                // ZP 20171220: 如果数据库中存在,需先解绑。
                if (!productOnlineBll.UnbindPallet(this.rfidUID, ref reStr))
                {
                    this.logRecorder.AddDebugLog(nodeName, reStr);
                    return false;
                }

                //生成新任务
                this.currentTaskPhase = 1;
                ControlTaskModel task = new ControlTaskModel();
                task.DeviceID = this.nodeID;
                task.CreateMode = "自动";
                task.CreateTime = System.DateTime.Now;
                task.TaskID = System.Guid.NewGuid().ToString("N");
                task.TaskType = (int)SysCfg.EnumAsrsTaskType.托盘装载;
                task.TaskParam = this.rfidUID;
                task.TaskPhase = this.currentTaskPhase;
                task.TaskStatus = SysCfg.EnumTaskStatus.执行中.ToString();
                task.Remark = SysCfg.EnumAsrsTaskType.托盘装载.ToString();
                task.tag1 = stockName;

                this.ctlTaskBll.Add(task);
                this.currentTask = task;
                currentTaskDescribe = "装载任务生成";
                return true;
            }
            if (db2Vals[0] == 1 && this.currentTask == null)
            {
                this.db1ValsToSnd[1] = 1;
            }
            return true;
        }

        // 将信息放入到OnLine数据库
        bool InsertToDB(string[] batteryList)
        {
            for (int i = 0; i < batteryList.Length; i++)
            {
                string batteryID = batteryList[i];
                MesDBAccess.Model.ProductOnlineModel productModel = null;
                if (productOnlineBll.Exists(batteryID))
                {
                    productModel = productOnlineBll.GetModel(batteryID);
                    productModel.productID = batteryID;
                    productModel.palletID = this.rfidUID;
                    productModel.batchName = (i + 1).ToString();
                    productModel.modifyTime = System.DateTime.Now;
                    productModel.processStepID = this.mesProcessStepID[0];
                    productModel.productCata = SysCfg.EnumProductCata.电芯.ToString();
                    productModel.palletBinded = true;
                    productModel.stationID = this.nodeID;
                    productModel.checkResult = "0";
                    //if (batteryID.Length > 22)
                    //{
                    //    productModel.batchName = batteryID.Substring(16, 6);
                    //}

                    //int seq = i + 1;
                    //productModel.tag1 = seq.ToString();
                    //int rowIndex = i / 12 + 1;
                    //productModel.tag2 = rowIndex.ToString();
                    //int colIndex = i - (rowIndex - 1) * 12 + 1;
                    //productModel.tag3 = colIndex.ToString();
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
                    productModel.palletID = this.rfidUID;
                    productModel.processStepID = this.mesProcessStepID[0];
                    productModel.productCata = SysCfg.EnumProductCata.电芯.ToString();
                    productModel.palletBinded = true;
                    productModel.stationID = this.nodeID;
                    productModel.checkResult = "0";
                    //if (batteryID.Length > 22)
                    //{
                    //    productModel.batchName = batteryID.Substring(16, 6);
                    //}
                    //int seq = i + 1;
                    //productModel.tag1 = seq.ToString();
                    //int rowIndex = i / 12 + 1;
                    //productModel.tag2 = rowIndex.ToString();
                    //int colIndex = i - (rowIndex - 1) * 12 + 1;
                    //productModel.tag3 = colIndex.ToString();
                    if (!productOnlineBll.Add(productModel))
                    {
                        return false;
                    }
                }
            }
            return true;
        }


        int GetBatteryInfoFromMES(string rfid, ref string retString, ref string[] singleSNCollect)
        {
            string sn = string.Empty;

            if (mesenable == 0)
            {
                for (int i = 0; i < snSize; i++)
                {
                    singleSNCollect[i] = "HSK" + System.DateTime.Now.ToString("yyyyMMdd") + (i + 1).ToString();
                }
                return 0;
            }
            else
            {
                sn = MESWCFManage.Inst().GetSNByRFID(this.rfidUID);
            }


            if (sn.Length <= 0)
            {
                retString = "从MES中获取的条码SN为空";
                return 1;
            }

            string[] snCollect = sn.Split(',');
            if (snCollect.Length > this.snSize)
            {
                retString = string.Format("从MES中获取的条码SN个数不正确:等待获取 {0},实际获取 {1}", this.snSize, snCollect.Length);
                return 2;
            }


            for (int i = 0; i < snCollect.Length; i++)
            {
                singleSNCollect[i] = snCollect[i];
            }




            return 0;
        }


    }
}

