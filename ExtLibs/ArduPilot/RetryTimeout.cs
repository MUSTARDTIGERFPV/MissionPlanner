using System;
using System.Reflection;
using System.Threading;
using log4net;

namespace MissionPlanner.ArduPilot
{
    public class RetryTimeout
    {
        /// <summary>
        /// Has the task completed
        /// </summary>
        public bool Complete
        {
            get { return _complete; }
            set
            {
                _complete = value;
                if (value)
                    _signal.Set();
            }
        }
        private volatile bool _complete = false;
        /// <summary>
        /// Set when Complete is set, so DoWork wakes without polling
        /// </summary>
        private readonly ManualResetEventSlim _signal = new ManualResetEventSlim(false);
        /// <summary>
        /// Number of total retrys
        /// </summary>
        public int Retries = 3;
        /// <summary>
        /// Current retry count
        /// </summary>
        public int RetriesCurrent = 0;
        /// <summary>
        /// timeout between retrys
        /// </summary>
        public int TimeoutMS = 1000;

        private readonly string _memberName;

        /// <summary>
        /// Action to do on retry
        /// </summary>
        public Action WorkToDo;
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private DateTime _timeOutDateTime = DateTime.MinValue;

        public RetryTimeout(int Retrys = 30, int TimeoutMS = 1000, [System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        {
            this.Retries = Retrys;
            this.TimeoutMS = TimeoutMS;
            _memberName = memberName;
        }

        public DateTime TimeOutDateTime
        {
            get
            {
                lock (this) return _timeOutDateTime;
            }
            set
            {
                lock (this) _timeOutDateTime = value;
            }
        }

        public bool DoWork()
        {
            if (WorkToDo == null)
                throw new ArgumentNullException("WorkToDo");
            Complete = false;
            for (RetriesCurrent = 0; RetriesCurrent < Retries; RetriesCurrent++)
            {
                log.InfoFormat("Retry {0} {1} - {2}", _memberName, RetriesCurrent,
                    TimeOutDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff"));
                WorkToDo();
                TimeOutDateTime = DateTime.Now.AddMilliseconds(TimeoutMS);
                while (true)
                {
                    // reset before checking, so a Set() after the check still ends the Wait()
                    _signal.Reset();
                    if (Complete)
                        return true;
                    // re-read each time, ResetTimeout() can move it out
                    var remaining = TimeOutDateTime - DateTime.Now;
                    if (remaining <= TimeSpan.Zero)
                        break;
                    _signal.Wait(remaining);
                }
            }

            return false;
        }

        public void ResetTimeout()
        {
            TimeOutDateTime = DateTime.Now.AddMilliseconds(TimeoutMS);
        }
    }
}