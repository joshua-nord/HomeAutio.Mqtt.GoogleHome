﻿using System.Collections.Generic;
using HomeAutio.Mqtt.GoogleHome.JsonConverters;
using HomeAutio.Mqtt.GoogleHome.Models.State.ValueMaps;
using Newtonsoft.Json;

namespace HomeAutio.Mqtt.GoogleHome.Models.State
{
    /// <summary>
    /// Device state configuration.
    /// </summary>
    public class DeviceState
    {
        /// <summary>
        /// MQTT topic.
        /// </summary>
        public string Topic { get; set; }

        /// <summary>
        /// Google device state.
        /// </summary>
        public GoogleType GoogleType { get; set; }

        /// <summary>
        /// Value mappings.
        /// </summary>
        [JsonProperty(ItemConverterType = typeof(ValueMapJsonConverter))]
        public IList<MapBase> ValueMap { get; set; }

        /// <summary>
        /// Handles mapping some common state values to google acceptable state values.
        /// </summary>
        /// <param name="stateValue">State value.</param>
        /// <returns>Remapped value.</returns>
        public object MapValueToGoogle(string stateValue)
        {
            // Run any transforms first
            if (ValueMap != null && ValueMap.Count > 0)
            {
                foreach (var valueMap in ValueMap)
                {
                    if (valueMap.MatchesMqtt(stateValue))
                    {
                        // Do value comparison, break on first match
                        stateValue = valueMap.ConvertToGoogle(stateValue);
                        break;
                    }
                }
            }

            // Convert to Google type
            switch (GoogleType)
            {
                case GoogleType.Bool:
                    if (bool.TryParse(stateValue, out bool boolValue))
                        return boolValue;
                    else
                        return default(bool);
                case GoogleType.Numeric:
                    if (int.TryParse(stateValue, out int intValue))
                        return intValue;
                    else if (decimal.TryParse(stateValue, out decimal decimalValue))
                        return decimalValue;
                    else
                        return default(int);
                case GoogleType.String:
                    return stateValue;
            }

            return null;
        }

        /// <summary>
        /// Handles mapping some common state values to google acceptable state values.
        /// </summary>
        /// <param name="stateValue">State value.</param>
        /// <returns>Remapped value.</returns>
        public string MapValueToMqtt(object stateValue)
        {
            // Default to string version of passed parameter value
            var mappedValue = stateValue.ToString();

            if (ValueMap != null && ValueMap.Count > 0)
            {
                foreach (var valueMap in ValueMap)
                {
                    if (valueMap.MatchesGoogle(stateValue))
                    {
                        // Do value comparison, break on first match
                        mappedValue = valueMap.ConvertToMqtt(stateValue);
                        break;
                    }
                }
            }

            return mappedValue;
        }
    }
}
