using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

namespace BoardingRangeFix
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class FlightBoardingBypass : MonoBehaviour
    {
        private float scanTimer = 0f;
        private Part cachedTargetPart = null;
        private Part cachedTargetSeat = null;
        private bool isBoarding = false;

        // Reflection Caching
        private FieldInfo boardPartField = null;
        private PropertyInfo boardPartProp = null;
        private MethodInfo boardMethod = null;

        // Cached Data & Buffers to prevent Garbage Collection stutters
        private readonly string[] hatchNames = { "Airlock", "airlock", "Hatch", "hatch", "EVAairlock" };
        private RaycastHit[] hitBuffer = new RaycastHit[15];

        private void LateUpdate()
        {
            if (FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.isEVA && !isBoarding)
            {
                KerbalEVA eva = FlightGlobals.ActiveVessel.GetComponent<KerbalEVA>();
                if (eva == null) return;

                if (IsKerbalOnLadder(eva)) return;

                // 1. HOSTILE TAKEOVER (Suppresses stock prompt from rendering)
                SuppressStockBoarding(eva);

                scanTimer -= Time.deltaTime;
                if (scanTimer <= 0f)
                {
                    // 2. PRIMARY SCAN: Look for a standard hatch at 0.9m
                    cachedTargetPart = FindValidHatch(0.9f);

                    // 3. SECONDARY SCAN: If no hatch, look for an empty command seat at 0.9m
                    if (cachedTargetPart == null)
                    {
                        cachedTargetSeat = FindValidSeat(0.9f);
                    }
                    else
                    {
                        cachedTargetSeat = null;
                    }

                    scanTimer = 0.2f;
                }

                string keyName = GameSettings.EVA_Board.primary.ToString();

                // 4a. EXECUTE HATCH BOARDING
                if (cachedTargetPart != null)
                {
                    // FIXED: Shifted to UPPER_CENTER to render 25% down from the screen top
                    ScreenMessages.PostScreenMessage("[" + keyName + "]: Board", 0.1f, ScreenMessageStyle.UPPER_CENTER);

                    if (GameSettings.EVA_Board.GetKeyDown())
                    {
                        isBoarding = true;
                        TryBoard(eva, cachedTargetPart);
                        cachedTargetPart = null;
                        Invoke("ResetBoardingState", 2.0f);
                    }
                }
                // 4b. EXECUTE SEAT BOARDING
                else if (cachedTargetSeat != null)
                {
                    // FIXED: Shifted to UPPER_CENTER to render 25% down from the screen top
                    ScreenMessages.PostScreenMessage("[" + keyName + "]: Board Seat", 0.1f, ScreenMessageStyle.UPPER_CENTER);

                    if (GameSettings.EVA_Board.GetKeyDown())
                    {
                        isBoarding = true;
                        TryBoardSeat(cachedTargetSeat);
                        cachedTargetSeat = null;
                        Invoke("ResetBoardingState", 2.0f);
                    }
                }
            }
        }

        private void SuppressStockBoarding(KerbalEVA eva)
        {
            if (boardPartField == null && boardPartProp == null)
            {
                System.Type evaType = eva.GetType();
                boardPartField = evaType.GetField("boardPart", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                boardPartProp = evaType.GetProperty("boardPart", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            }

            if (boardPartField != null && boardPartField.GetValue(eva) != null)
            {
                boardPartField.SetValue(eva, null);
            }
            else if (boardPartProp != null && boardPartProp.GetValue(eva, null) != null)
            {
                boardPartProp.SetValue(eva, null, null);
            }
        }

        private bool IsKerbalOnLadder(KerbalEVA eva)
        {
            return eva.fsm != null && eva.fsm.CurrentState != null && eva.fsm.CurrentState.name.ToLower().Contains("ladder");
        }

        private void ResetBoardingState()
        {
            isBoarding = false;
        }

        private Part FindValidHatch(float maxDist)
        {
            Part bestPart = null;
            float currentMin = maxDist;
            int layerMask = 1 << 0;

            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == FlightGlobals.ActiveVessel || !v.loaded) continue;

                foreach (Part p in v.parts)
                {
                    if (p.CrewCapacity > 0 && p.CrewCapacity > p.protoModuleCrew.Count)
                    {
                        foreach (string hName in hatchNames)
                        {
                            Transform[] transforms = p.FindModelTransforms(hName);
                            if (transforms == null) continue;

                            foreach (Transform hatch in transforms)
                            {
                                float dist = Vector3.Distance(FlightGlobals.ActiveVessel.transform.position, hatch.position);
                                if (dist < currentMin)
                                {
                                    Vector3 dir = hatch.position - FlightGlobals.ActiveVessel.transform.position;
                                    bool isBlocked = false;

                                    int hitCount = Physics.RaycastNonAlloc(FlightGlobals.ActiveVessel.transform.position, dir.normalized, hitBuffer, dist, layerMask);

                                    for (int i = 0; i < hitCount; i++)
                                    {
                                        Part hitPart = hitBuffer[i].collider.gameObject.GetComponentInParent<Part>();
                                        if (hitPart != null && hitPart != p)
                                        {
                                            isBlocked = true;
                                            break;
                                        }
                                    }

                                    if (!isBlocked)
                                    {
                                        currentMin = dist;
                                        bestPart = p;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return bestPart;
        }

        private Part FindValidSeat(float maxDist)
        {
            Part bestSeat = null;
            float currentMin = maxDist;
            int layerMask = 1 << 0;

            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == FlightGlobals.ActiveVessel || !v.loaded) continue;

                foreach (Part p in v.parts)
                {
                    if (p.Modules.Contains("KerbalSeat") && p.protoModuleCrew.Count == 0)
                    {
                        float dist = Vector3.Distance(FlightGlobals.ActiveVessel.transform.position, p.transform.position);
                        if (dist < currentMin)
                        {
                            Vector3 dir = p.transform.position - FlightGlobals.ActiveVessel.transform.position;
                            bool isBlocked = false;

                            int hitCount = Physics.RaycastNonAlloc(FlightGlobals.ActiveVessel.transform.position, dir.normalized, hitBuffer, dist, layerMask);

                            for (int i = 0; i < hitCount; i++)
                            {
                                Part hitPart = hitBuffer[i].collider.gameObject.GetComponentInParent<Part>();
                                if (hitPart != null && hitPart != p)
                                {
                                    isBlocked = true;
                                    break;
                                }
                            }

                            if (!isBlocked)
                            {
                                currentMin = dist;
                                bestSeat = p;
                            }
                        }
                    }
                }
            }
            return bestSeat;
        }

        private void TryBoard(KerbalEVA eva, Part targetPart)
        {
            if (boardMethod == null)
            {
                foreach (MethodInfo m in eva.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    string name = m.Name.ToLower();
                    if ((name == "boardpart" || name == "board") && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Part))
                    {
                        boardMethod = m;
                        break;
                    }
                }
            }

            if (boardMethod != null)
            {
                boardMethod.Invoke(eva, new object[] { targetPart });
            }
        }

        private void TryBoardSeat(Part seatPart)
        {
            PartModule seatModule = seatPart.Modules["KerbalSeat"];
            if (seatModule != null)
            {
                BaseEvent boardEvent = seatModule.Events["BoardSeat"];
                if (boardEvent != null)
                {
                    boardEvent.Invoke();
                }
                else
                {
                    MethodInfo m = seatModule.GetType().GetMethod("BoardSeat", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m != null) m.Invoke(seatModule, null);
                }
            }
        }
    }
}