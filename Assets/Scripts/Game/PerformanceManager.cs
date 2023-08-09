﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Timers;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using static WallManager;

public enum JudgementType {
    AverageSpeed,
    MaxSpeed,
    Distance,
    Time
}

// Data Structures
[System.Serializable]
public class PerfData {
    public Queue<float> lastVals = new Queue<float>();
    public Queue<float> lastJudges = new Queue<float>();
    public float movingAverage = -1f;
    public float upperThresholdAction = -1f;
    public float lowerThresholdAction = -1f;
    public Queue<float> meanMemoryVals = new Queue<float>();
    public float perfBestAction = -1f;
    public float perfActionFraction = -1f;
    public float dwelltime = 0f;

    // State
    public float perfPrev = -1f;
    public float perf = -1f;
    public float perfBest = -1f;
    public float perfFraction = -1f;
    public float judge = -1f;
    public float upperThresholdInstant = -1f;
    public float lowerThresholdInstant = -1f;
    public Vector3 posPrev = Vector3.zero;
    public Vector3 pos = Vector3.zero;

    public Vector3 actionStartPos = Vector3.zero;
    public Vector3 actionEndPos = Vector3.zero;
    public float actionStartTimestamp = -1f;
    public float actionEndTimestamp = -1f;
}

public class PerformanceManager : MonoBehaviour
{
    [SerializeField]
    private JudgementType judgementType = JudgementType.AverageSpeed;

    // Action data
    private Dictionary<ControllerName, PerfData> perfData = new Dictionary<ControllerName, PerfData>();
    public PerfData perfR = new PerfData();
    public PerfData perfL = new PerfData();

    // Average configuration
    private float meanMemoryLimit = 20; // use the last 20 values for calculating mean
    private int minimumJudgeThreshold = 5;
    private float MultiplierUp = 2f; // Upper/Lower Threshold multipliers
    private float MultiplierDown = 0.50f;
    private float fadingFraction = 0.1f; // how much the max should fade over time (1%)

    private void Awake()
    {
        perfData[ControllerName.Left] = perfL;
        perfData[ControllerName.Right] = perfR;
    }

    // Data Consumption
    public PerfData GetPerfData(ControllerName controllerName) {
        return perfData[controllerName];
    }

    public float GetInstantJudgement(ControllerName controllerName) {
        return perfData[controllerName].judge;
    }

    public float GetActionJudgement(ControllerName controllerName) {
        return perfData[controllerName].lastJudges.LastOrDefault();
    }

    // Data Control
    public void ResetPerfHistory() {
        foreach(KeyValuePair<ControllerName, PerfData> entry in perfData) {
            entry.Value.lastVals.Clear();
            entry.Value.lastJudges.Clear();
        }       
    }

    public void ResetPerfData() {
        // Resets History, but maintains moving average.
        perfR = new PerfData();
        perfL = new PerfData();
        perfData[ControllerName.Left] = perfL;
        perfData[ControllerName.Right] = perfR;
    }

    // Data Feeders
    // BasicPointer: OnPointerShoot and OnPointerMove
    public void OnPointerShoot(ShootData shootData) {
        RaycastHit hit = shootData.hit;
        ControllerName controllerName = shootData.name;

        bool moleHit = false;
        Mole mole;

        if (hit.collider) { 
            if (hit.collider.gameObject.TryGetComponent<Mole>(out mole)) {
                moleHit = true;
            }
        }
        
        // if we dont hit a mole, don't count as an action.
        if (!moleHit) return;

        PerfData perf = perfData[controllerName];

        perf.dwelltime = shootData.dwell;
        perf.actionEndPos = perf.actionStartPos;
        perf.actionStartPos = hit.point;
        perf.actionStartTimestamp = perf.actionEndTimestamp;
        perf.actionEndTimestamp = Time.time;

        float newVal;
        float judgement;

        if (judgementType == JudgementType.AverageSpeed) {
            newVal = CalculateActionSpeed(perf);
            UpdateActionMovingAverage(newVal, perf);
            judgement = MakeActionJudgement(newVal, perf);
        } else if (judgementType == JudgementType.MaxSpeed) {
            newVal = CalculateActionSpeed(perf);
            UpdateActionThresholds(newVal, perf);
            judgement = MakeActionJudgement(newVal, perf);
        } else if (judgementType == JudgementType.Distance) {
            newVal = CalculateActionDistance(perf);
            UpdateActionMovingAverage(newVal, perf);
            judgement = MakeActionJudgement(newVal, perf);
        } else if (judgementType == JudgementType.Time) {
            newVal = CalculateActionTime(perf);
            UpdateActionThresholds(newVal, perf, thresholdMax: false);
            judgement = MakeActionJudgement(newVal, perf, thresholdMax: false);
        } else {
            newVal = -1f;
            judgement = 0f;
        }

        // Store Results
        perf.lastVals.Enqueue(newVal);
        perf.lastJudges.Enqueue(judgement);
    }

    public void SetJudgementType(JudgementType judgement) {
        judgementType = judgement;
    }

    public void OnPointerMove(MoveData moveData) {
        PerfData perf = perfData[moveData.name];

        perf.posPrev = perf.pos;
        perf.pos = moveData.cursorPos;
        perf.perfPrev = perf.perf;

        float newPerf;
        float judgement;

        if (judgementType == JudgementType.AverageSpeed) {
            newPerf = CalculateInstantAvgSpeed(perf);
            UpdateInstantAvgSpeedThresholds(newPerf, perf);
            judgement = MakeInstantJudgement(newPerf, perf);
        } else if (judgementType == JudgementType.MaxSpeed) {
            newPerf = CalculateInstantMaxSpeed(perf);
            UpdateInstantThresholds(newPerf, perf);
            judgement = MakeInstantJudgement(newPerf, perf);
        } else if (judgementType == JudgementType.Distance) {
            newPerf = CalculateInstantDistance(perf);
            UpdateInstantAvgSpeedThresholds(newPerf, perf);
            judgement = MakeInstantJudgement(newPerf, perf);
        } else if (judgementType == JudgementType.Time) {
            newPerf = CalculateInstantTime(perf);
            UpdateInstantThresholds(newPerf, perf, thresholdMax: false);
            judgement = MakeInstantJudgement(newPerf, perf, thresholdMax: false);
        } else {
            newPerf = -1f;
            judgement = 0f;
        }

        perf.perf = newPerf;
        perf.judge = judgement;
    }

    // Average Calculator
    private void UpdateActionMovingAverage(float val, PerfData perf) {

        // Update moving average
        perf.meanMemoryVals.Enqueue(val);
        if (perf.meanMemoryVals.Count > meanMemoryLimit)
        {
            perf.meanMemoryVals.Dequeue();
        }

        perf.movingAverage = perf.meanMemoryVals.Average();
        perf.upperThresholdAction = MultiplierUp * perf.movingAverage;
        perf.lowerThresholdAction = MultiplierDown * perf.movingAverage;
    }

    // Max-based Calculator
    private void UpdateActionThresholds(float val, PerfData perf, bool thresholdMax = true) {

        if (val == -1f) return;

        // Update memory, just to ensure same number of performances are required.
        perf.meanMemoryVals.Enqueue(val);
        if (perf.meanMemoryVals.Count > meanMemoryLimit)
        {
            perf.meanMemoryVals.Dequeue();
        }

        Debug.Log(val + " " + perf.perfBestAction);
        if (thresholdMax && perf.perfBestAction == -1f) {
            perf.perfBestAction = val;
            perf.perfActionFraction = perf.perfBestAction * fadingFraction;
        } else if (!thresholdMax && perf.perfBestAction == -1f) { 
            perf.perfBestAction = val;
            perf.perfActionFraction = perf.perfBestAction * (1 - fadingFraction);
        } else if (thresholdMax && val > perf.perfBestAction) {
            perf.perfBestAction = val;
            perf.perfActionFraction = perf.perfBestAction * fadingFraction;
        } else if (!thresholdMax && val < perf.perfBestAction) {
            perf.perfBestAction = val;
            perf.perfActionFraction = perf.perfBestAction * (1 - fadingFraction);
        } else {
            float time = perf.actionEndTimestamp - perf.actionStartTimestamp;
            if (thresholdMax) { 
            perf.perfBestAction -= time * fadingFraction;
            } else
            {
                perf.perfBestAction += time * fadingFraction;
            }
        }

        //Fading: subtracts 0.1 m/s per second
        //  5, -0.05
        //perf.movingAverage = perf.meanMemoryVals.Average();
        if (thresholdMax)
        {
            perf.upperThresholdAction = perf.perfBestAction;
            perf.lowerThresholdAction = MultiplierDown * perf.perfBestAction;
        }
        else
        {
            perf.upperThresholdAction = MultiplierUp * perf.perfBestAction;
            perf.lowerThresholdAction = perf.perfBestAction;
        }

    }

    private void UpdateInstantThresholds(float val, PerfData perf, bool thresholdMax = true)
    {
        if (val == -1f) return;

        if (thresholdMax && perf.perfBest == -1f)
        {
            // Set the value as the new performance max.
            perf.perfBest = val;
            perf.perfFraction = perf.perfBest * fadingFraction;
        } else if (!thresholdMax && perf.perfBest == -1f)
        {
            // Set the value as the new performance max.
            perf.perfBest = val;
            perf.perfFraction = perf.perfBest * (1 - fadingFraction);
        } else if (thresholdMax && val > perf.perfBest)
        {
            // Set the value as the new performance max.
            perf.perfBest = val;
            perf.perfFraction = perf.perfBest * fadingFraction;
        } else if (!thresholdMax && val < perf.perfBest)
        {
            perf.perfBest = val;
            perf.perfFraction = perf.perfBest * (1 - fadingFraction);
        }
        else
        {
            if (thresholdMax) { 
                // if the value was not higher, reduce the maximum value by a fraction.
                perf.perfBest -= Time.deltaTime * fadingFraction;
            } else
            {
                perf.perfBest += Time.deltaTime * fadingFraction;
            }
        }

        if (thresholdMax) { 
            perf.upperThresholdInstant = perf.perfBest;
            perf.lowerThresholdInstant = MultiplierDown * perf.perfBest;
        } else
        {
            perf.upperThresholdInstant = MultiplierUp * perf.perfBest;
            perf.lowerThresholdInstant = perf.perfBest;
        }
    }

    private void UpdateInstantAvgSpeedThresholds(float val, PerfData perf)
    {
        perf.upperThresholdInstant = perf.upperThresholdAction;
        perf.lowerThresholdInstant = perf.lowerThresholdAction;
    }

    // Max-based Calculator
    private float CalculateInstantMaxSpeed(PerfData perf) {

        // if we don't have a previous position, abort calculation.
        if (perf.actionStartPos == Vector3.zero) return -1f;

        //Debug.Log("lastPosition: " + lastPositionSpeed);
        float distance = Vector3.Distance(perf.pos, perf.posPrev);
        float speed = distance / Time.deltaTime;
        return speed;
    }

    // Calculators
    private float CalculateInstantAvgSpeed(PerfData perf) {
        // TODO: Should we calculate the instant speed (frame by frame), or should we calculate speed
        // based on the distance accumulated since the beginning?

        // if we don't have a previous position, abort calculation.
        if (perf.actionStartPos == Vector3.zero) return -1f;

        //Debug.Log("lastPosition: " + lastPositionSpeed);
        float distance = Vector3.Distance(perf.pos, perf.actionStartPos);
        float time = Time.time - perf.actionStartTimestamp;
        float speed = distance / time;
        return speed;
    }

    private float CalculateInstantDistance(PerfData perf) {
        // TODO: Should we calculate the instant speed (frame by frame), or should we calculate speed
        // based on the distance accumulated since the beginning?

        // if this is our first action, we don't have enough information to calculate speed.
        if (perf.actionStartPos == Vector3.zero) return -1f;

        // if we don't have a previous position, abort calculation.
        if (perf.posPrev == Vector3.zero) return -1f;

        if (perf.perf == -1f) perf.perf = 0f;
        float distance = perf.perf + Vector3.Distance(perf.pos, perf.posPrev);

        //Debug.Log("lastPosition: " + lastPositionSpeed);
        //float distance = Vector3.Distance(perf.actionStartPos, perf.pos);
        return distance;
    }

    private float CalculateInstantTime(PerfData perf) {
        // TODO: Should we calculate the instant speed (frame by frame), or should we calculate speed
        // based on the distance accumulated since the beginning?

        // if this is our first action, we don't have enough information to calculate speed.
        if (perf.actionStartTimestamp == -1f) return -1f;

        // if we don't have a previous position, abort calculation.
        if (perf.posPrev == Vector3.zero) return -1f;

        float time = Time.time - perf.actionStartTimestamp;
        time = time;
        if (time < 0f) time = 0f;
        return time;
    }

    private float CalculateActionDistance(PerfData perf) {
        if (perf.actionEndTimestamp == -1f || perf.actionEndPos == Vector3.zero) {
            // if this is our first action, we don't have enough information to calculate speed.
            return -1f;
        }

        float distance = Vector3.Distance(perf.actionStartPos, perf.actionEndPos);
        return distance;
    }

    private float CalculateActionTime(PerfData perf) {
        if (perf.actionEndTimestamp == -1f || perf.actionEndPos == Vector3.zero) {
            // if this is our first action, we don't have enough information to calculate speed.
            return -1f;
        }

        float time = perf.actionEndTimestamp - perf.actionStartTimestamp;
        time = time;
        if (time < 0f) time = 0f;
        return time;
    }

    private float CalculateActionSpeed(PerfData perf) {
        if (perf.actionEndTimestamp == -1f || perf.actionEndPos == Vector3.zero) {
            // if this is our first action, we don't have enough information to calculate speed.
            return -1f;
        }

        float distance = Vector3.Distance(perf.actionStartPos, perf.actionEndPos);
        float time = perf.actionEndTimestamp - perf.actionStartTimestamp;
        Debug.Log(time);
        time = time - perf.dwelltime; // subtract dwell time.
        float speed = distance / time;

        return speed;
    }

    private float MakeActionJudgement(float val, PerfData perf, bool thresholdMax = true) {
        float judgement;

        // If there is less than the threshold to judge threshold, default to 100% postive feedback.
        if (perf.meanMemoryVals.Count < minimumJudgeThreshold)
        {
            judgement = 0f;
            return judgement;
        }

        if (val <= perf.lowerThresholdAction)
        {
            judgement = thresholdMax ? 0 : 1;
        }
        else if (val >= perf.upperThresholdAction)
        {
            judgement = thresholdMax ? 1 : 0;
        }
        else
        {
            judgement = (val - perf.lowerThresholdAction) / (perf.upperThresholdAction - perf.lowerThresholdAction);
            if (!thresholdMax) {
                judgement = 1 - judgement;
            }
        }

        return judgement;
    }

    private float MakeInstantJudgement(float val, PerfData perf, bool thresholdMax = true)
    {
        float judgement;

        if (val == -1f)
        {
            judgement = 0;
            return judgement;
        }

        if (val <= perf.lowerThresholdInstant)
        {
            judgement = thresholdMax ? 0 : 1;
        }
        else if (val >= perf.upperThresholdInstant)
        {
            judgement = thresholdMax ? 1 : 0;
        }
        else
        {
            judgement = (val - perf.lowerThresholdInstant) / (perf.upperThresholdInstant - perf.lowerThresholdInstant);
            if (!thresholdMax)
            {
                judgement = 1 - judgement;
            }
        }

        return judgement;
    }

}




//     // old code
//     public void OnPointerShoot()
//     {
//         isTimerRunning = false;
//         CalculateAction();
//     }

//     private float timeSinceLastShot = 0f;
//     private bool isTimerRunning = false;
//     private float speed = 0f;
//     private float instantSpeed = 0f;
//     private Vector3 lastPosition = Vector3.zero;
//     private Vector3 lastPositionSpeed = Vector3.zero;
//     private float lastDistance = 0f;
//     private float feedback = 0f;
//     private float averageSpeed = 0f;
//     private int nbShoot = 0;

//     private void Awake()
//     {
//     }

//     private void Update()
//     {
//         if (isTimerRunning)
//         {
//             timeSinceLastShot += Time.deltaTime;
//         }

//         CalculateSpeed();
//         CalculateInstantSpeed();
//     }

//     private void ResetShoot()
//     {
//         timeSinceLastShot = 0f;
//         speed = 0f;
//         lastDistance = 0f;
//     }



//     public void onMoleActivated()
//     {
//         isTimerRunning = true;
//         timeSinceLastShot = 0f;
//         lastDistance = 0f;
//     }


//     public void UpdatePointerData(BasicPointer pointer)
//     {
//         // Now you have access to all public variables and methods of the BasicPointer instance
//         pointerData = pointer;

//     }

//     public void CalculateSpeed()
//     {

//         Vector3 position = pointerData.MappedPosition;
//         if (lastPosition == Vector3.zero)
//         {
//             lastPosition = position;
//         }
//         if (isTimerRunning)
//         {
//             float distance = Vector3.Distance(position, lastPosition);
//             lastPosition = position;
//             lastDistance = lastDistance + distance;
//             speed = lastDistance / timeSinceLastShot;
//         }
//     }

//     public void CalculateInstantSpeed()
//     {
//         Vector3 position = pointerData.MappedPosition;
//         if (lastPositionSpeed == Vector3.zero)
//         {
//             lastPositionSpeed = position;
//         }
//         if (lastPositionSpeed != Vector3.zero)
//         {
//             Debug.Log("lastPosition: " + lastPositionSpeed);
//             float distance = Vector3.Distance(position, lastPositionSpeed);
//             instantSpeed = distance / Time.deltaTime;
//         }
//         else
//         {
//             Debug.Log("FESSE " + lastPositionSpeed);
//         }
//         lastPositionSpeed = position;
//     }


//     public float GetSpeed()
//     {
//         return speed;
//     }

//     public float GetInstantSpeed()
//     {
//         return instantSpeed;
//     }


//     public float GetFeedback()
//     {
//         return feedback;
//     }

//     public Queue<float> GetTaskFeedbacks() {
//         return taskFeedbacks;
//     }

//     public void CalculateFeedback()
//     {
//         float minDistance = 0.3f;
//         lastSpeeds.Enqueue(speed);

//         if (lastSpeeds.Count > 20)
//         {
//             lastSpeeds.Dequeue();
//         }
//         if (nbShoot < 5)
//         {
//             feedback = 1;
//             averageSpeed = speed;
//             nbShoot++;
//         }
//         else if (lastDistance <= minDistance)
//         {
//             feedback = 1;
//         }
//         else
//         {
//             averageSpeed = lastSpeeds.Average();
//             nbShoot++;
//             float thresholdUp = 1.50f * averageSpeed;
//             float thresholdDown = 0.50f * averageSpeed;

//             if (speed <= thresholdDown)
//             {
//                 feedback = 0;
//             }
//             else if (speed >= thresholdUp)
//             {
//                 feedback = 1;
//             }
//             else
//             {
//                 feedback = (speed - thresholdDown) / (thresholdUp - thresholdDown);
//             }

//         }
//         taskFeedbacks.Enqueue(feedback);
//         ResetShoot();
//     }
// }