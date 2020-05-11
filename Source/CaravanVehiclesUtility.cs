﻿using System;
using System.Collections.Generic;
using Verse;
using RimWorld.Planet;

namespace RoadsOfTheRim
{
    public static class CaravanVehiclesUtility
    {
        public static int NumberOfSeats(Caravan c)
        {
            int result = 0;
            WorldObjectComp_Caravan CaravanComp = c.GetComponent<WorldObjectComp_Caravan>();
            if (CaravanComp != null)
            {
                List<Thing> ListOfVehicles = CaravanComp.GetListOfVehicles();
                foreach (Thing vehicle in ListOfVehicles)
                {
                    result += ThingCompUtility.TryGetComp<ThingComp_RotR_Vehicles>(vehicle).Seats;
                }
            }
            return result;
        }

        public static float TotalVehicleSpeed(Caravan c)
        {
            float result = 0f;
            WorldObjectComp_Caravan CaravanComp = c.GetComponent<WorldObjectComp_Caravan>();
            if (CaravanComp != null)
            {
                int FueledSeats = 0;
                List<Thing> ListOfVehicles = CaravanComp.GetListOfVehicles();
                foreach (Thing vehicle in ListOfVehicles)
                {
                    ThingComp_RotR_Vehicles VehicleComp = ThingCompUtility.TryGetComp<ThingComp_RotR_Vehicles>(vehicle);
                    if (VehicleComp !=null)
                    {
                        // If the vehicle has fuel, add its seats to the total number of fueled seats and adjust speed if it's the slowest vehicle
                        if (VehicleComp.Fuel>0)
                        {
                            FueledSeats += VehicleComp.Seats;
                            if (result==0 || VehicleComp.Speed<result)
                            {
                                result = VehicleComp.Speed;
                            }
                        }
                    }
                }
            }
            return result;
        }

    }

}