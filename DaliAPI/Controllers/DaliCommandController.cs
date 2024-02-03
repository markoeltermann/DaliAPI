using Microsoft.AspNetCore.Mvc;
using System.IO.Ports;

namespace DaliAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DaliCommandController(ILogger<DaliCommandController> logger) : ControllerBase
    {
        private static readonly Dictionary<byte, SwitchState> switchStates = [];
        private static readonly object switchStateLock = new();
        private static readonly object daliLock = new();

        [HttpGet("{address}")]
        public ActionResult SendCommand(string address, string? command = null, string? value = null)
        {
            if (string.IsNullOrEmpty(command) && string.IsNullOrEmpty(value))
                return BadRequest("command or value must be present");

            var valueToParse = string.IsNullOrEmpty(command) ? value : command;
            if (valueToParse!.Length != 2)
                return BadRequest("value or command must be a single hex byte");

            byte daliCommandByte;
            try
            {
                daliCommandByte = Convert.ToByte(valueToParse, 16);
            }
            catch (Exception)
            {
                return BadRequest("invalid value or command");
            }

            var addressByte = GetAddressByte(address);
            if (!string.IsNullOrEmpty(command))
            {
                addressByte += 1;
                if (daliCommandByte > 0x1f)
                    return BadRequest("configuration commands are not allowed");
            }

            var portNames = SerialPort.GetPortNames().ToList();
            portNames.Sort();
            if (portNames.Count == 0)
                return NotFound("No serial ports found.");

            var portName = portNames[^1];
            logger.LogInformation("Connection to port {PortName}.", portName);

            using var port = new SerialPort(portName, 115200);
            port.Open();

            var controllerCommandBytes = new byte[] { 0xA7, 0x7A, 0x01, 0x02, 0x00, 0x03, 0xFF, addressByte, daliCommandByte, 0x00, 0x00, 0x0D, 0x13 };

            port.Write(controllerCommandBytes, 0, controllerCommandBytes.Length);
            logger.LogInformation("DALI command {Command} written.", command);

            port.Close();
            return Ok();
        }

        [HttpGet("Switch/{address}")]
        public ActionResult Switch(string action, string direction, string address)
        {
            logger.LogInformation("Switch address: {Address}, action: {Action}, direction: {Direction}", address, action, direction);

            action = action.ToLower();
            direction = direction.ToLower();
            address = address.ToLower();

            var isHold = action == "hold";
            var isUp = direction == "up";

            var addressByte = GetAddressByte(address);

            lock (switchStateLock)
            {
                var switchState = switchStates.GetValueOrDefault(addressByte);
                if (switchState == null)
                {
                    switchState = new SwitchState();
                    switchStates[addressByte] = switchState;
                }

                if (switchState.IsUpActive && !isUp)
                {
                    switchState.Reset();
                    return Ok("reset");
                }
                if (switchState.IsDownActive && isUp)
                {
                    switchState.Reset();
                    return Ok("reset");
                }

                if (isHold)
                {
                    if (switchState.IsDownActive || switchState.IsUpActive || switchState.IsDimmingDown || switchState.IsDimmingUp)
                    {
                        switchState.Reset();
                        return Ok("reset");
                    }

                    if (isUp)
                        switchState.IsUpActive = true;
                    else
                        switchState.IsDownActive = true;

                    Task.Run(() =>
                    {
                        Thread.Sleep(50);

                        for (int i = 0; i < 15; i++)
                        {
                            Thread.Sleep(200);
                            lock (switchStateLock)
                            {
                                if ((!switchState.IsUpActive && !switchState.IsDownActive) || (switchState.IsUpActive && !isUp) || (switchState.IsDownActive && isUp))
                                {
                                    if (isUp)
                                        switchState.IsDimmingUp = false;
                                    else
                                        switchState.IsDimmingDown = false;
                                    return;
                                }
                                else
                                {
                                    if (isUp)
                                        switchState.IsDimmingUp = true;
                                    else
                                        switchState.IsDimmingDown = true;
                                }
                            }
                            SendDaliCommand((byte)(addressByte + 1), isUp ? (byte)0x01 : (byte)0x02);
                        }
                        lock (switchStateLock)
                        {
                            if (isUp)
                                switchState.IsDimmingUp = false;
                            else
                                switchState.IsDimmingDown = false;
                        }
                    });
                    return Ok("hold registered");
                }
                else // release
                {
                    if (!switchState.IsDownActive && !switchState.IsUpActive)
                    {
                        return Ok("nothing was active");
                    }

                    if (isUp)
                        switchState.IsUpActive = false;
                    else
                        switchState.IsDownActive = false;

                    if (switchState.IsDimmingDown || switchState.IsDimmingUp)
                        return Ok("dimming has already started");

                    if (isUp)
                        SendDaliCommand((byte)(addressByte + 1), 0x0a);
                    else
                        SendDaliCommand(addressByte, 0x00);

                    return Ok("switch command sent");
                }
            }
        }

        private static byte GetAddressByte(string address)
        {
            if (address == "all")
            {
                return 0xfe;
            }
            else if (address.StartsWith('a'))
            {
                if (byte.TryParse(address[1..], out var lampAddress) && lampAddress is >= 0 and <= 63)
                {
                    return (byte)(lampAddress << 1);
                }
                else
                {
                    throw new BadRequestException("Invalid address");
                }
            }
            else if (address.StartsWith('g'))
            {
                if (byte.TryParse(address[1..], out var groupAddress) && groupAddress is >= 0 and <= 15)
                {
                    return (byte)((groupAddress << 1) | 0x80);
                }
                else
                {
                    throw new BadRequestException("Invalid address");
                }
            }
            else
            {
                return 0xfe;
            }
        }

        /// <summary>
        /// Sends a DALI command to a Sunricher DALI controller.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="value"></param>
        private static void SendDaliCommand(byte address, byte value)
        {
            lock (daliLock)
            {
                var portNames = SerialPort.GetPortNames().ToList();
                portNames.Sort();
                if (portNames.Count == 0)
                    return;

                var portName = portNames[^1];
                //logger.LogInformation("Connection to port {PortName}.", portName);

                using var port = new SerialPort(portName, 115200);
                port.Open();

                var controllerCommandBytes = new byte[] { 0xA7, 0x7A, 0x01, 0x02, 0x00, 0x03, 0xFF, address, value, 0x00, 0x00, 0x0D, 0x13 };

                port.Write(controllerCommandBytes, 0, controllerCommandBytes.Length);
                //logger.LogInformation("DALI command {Command} written.", command);

                port.Close();

                Thread.Sleep(10);
            }
        }

        private class SwitchState
        {
            public bool IsUpActive;
            public bool IsDownActive;
            public bool IsDimmingUp;
            public bool IsDimmingDown;

            public void Reset()
            {
                IsUpActive = false;
                IsDownActive = false;
                IsDimmingDown = false;
                IsDimmingUp = false;
            }
        }
    }
}