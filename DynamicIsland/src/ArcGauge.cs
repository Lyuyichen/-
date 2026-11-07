using System;
using System.Windows;
using System.Windows.Media;

namespace Island
{
    /// <summary>
    /// 圆弧仪表：一条背景弧 + 一条数值弧，端点圆头。
    /// 起始角 135°，顺时针扫 270°（像一个开口朝下的表盘）。
    /// </summary>
    internal class ArcGauge : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            "Value", typeof(double), typeof(ArcGauge),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
            "StrokeThickness", typeof(double), typeof(ArcGauge),
            new FrameworkPropertyMetadata(7.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty ArcBrushProperty = DependencyProperty.Register(
            "ArcBrush", typeof(Brush), typeof(ArcGauge),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
            "TrackBrush", typeof(Brush), typeof(ArcGauge),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StartAngleProperty = DependencyProperty.Register(
            "StartAngle", typeof(double), typeof(ArcGauge),
            new FrameworkPropertyMetadata(135.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SweepAngleProperty = DependencyProperty.Register(
            "SweepAngle", typeof(double), typeof(ArcGauge),
            new FrameworkPropertyMetadata(270.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public double StrokeThickness
        {
            get { return (double)GetValue(StrokeThicknessProperty); }
            set { SetValue(StrokeThicknessProperty, value); }
        }

        public Brush ArcBrush
        {
            get { return (Brush)GetValue(ArcBrushProperty); }
            set { SetValue(ArcBrushProperty, value); }
        }

        public Brush TrackBrush
        {
            get { return (Brush)GetValue(TrackBrushProperty); }
            set { SetValue(TrackBrushProperty, value); }
        }

        public double StartAngle
        {
            get { return (double)GetValue(StartAngleProperty); }
            set { SetValue(StartAngleProperty, value); }
        }

        public double SweepAngle
        {
            get { return (double)GetValue(SweepAngleProperty); }
            set { SetValue(SweepAngleProperty, value); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 1 || h <= 1) return;

            double thick = StrokeThickness;
            double size = Math.Min(w, h);
            double radius = (size - thick) / 2.0 - 1.0;
            if (radius <= 1) return;

            var center = new Point(w / 2.0, h / 2.0);

            var trackPen = new Pen(TrackBrush, thick)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            dc.DrawGeometry(null, trackPen, BuildArc(center, radius, StartAngle, SweepAngle));

            double v = Value;
            if (v < 0) v = 0; else if (v > 1) v = 1;
            if (v <= 0.001) return;

            var valuePen = new Pen(ArcBrush, thick)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            dc.DrawGeometry(null, valuePen, BuildArc(center, radius, StartAngle, SweepAngle * v));
        }

        static Geometry BuildArc(Point center, double radius, double startDeg, double sweepDeg)
        {
            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                Point start = PointOn(center, radius, startDeg);
                Point end = PointOn(center, radius, startDeg + sweepDeg);
                bool large = Math.Abs(sweepDeg) > 180.0;

                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(radius, radius), 0, large,
                    sweepDeg >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
            }
            geo.Freeze();
            return geo;
        }

        static Point PointOn(Point c, double r, double deg)
        {
            double rad = deg * Math.PI / 180.0;
            return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
        }
    }
}
