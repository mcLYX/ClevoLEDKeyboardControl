namespace ColorfulLedKeyboard.Core;

public sealed class RainbowColorWheel
{
    private int _r = 255;
    private int _g;
    private int _b;
    private int _phase;

    public RgbColor Next(int step)
    {
        step = Math.Clamp(step, 1, 20);

        switch (_phase)
        {
            case 0:
                _g += step;
                if (_g >= 255)
                {
                    _g = 255;
                    _phase = 1;
                }
                break;
            case 1:
                _r -= step;
                if (_r <= 0)
                {
                    _r = 0;
                    _phase = 2;
                }
                break;
            case 2:
                _b += step;
                if (_b >= 255)
                {
                    _b = 255;
                    _phase = 3;
                }
                break;
            case 3:
                _g -= step;
                if (_g <= 0)
                {
                    _g = 0;
                    _phase = 4;
                }
                break;
            case 4:
                _r += step;
                if (_r >= 255)
                {
                    _r = 255;
                    _phase = 5;
                }
                break;
            default:
                _b -= step;
                if (_b <= 0)
                {
                    _b = 0;
                    _phase = 0;
                }
                break;
        }

        return new RgbColor((byte)_r, (byte)_g, (byte)_b);
    }
}
