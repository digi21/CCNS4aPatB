using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Globalization;
using Digi21.OpenGis.Epsg;
using Digi21.OpenGis.CoordinateSystems;
using Digi21.Math;

namespace CCNS4aPatB
{
    struct DatosOrientación
    {
        public int NúmeroFrame;
        public int NúmeroPasada;
        public int NúmeroSeguimiento;
        public double LatitudWgs84; // En sexadecimal, grado y fracción decimal de grado.
        public double LongitudWgs84;// En sexadecimal, grado y fracción decimal de grado.
        public double Altitud;
        public double Heading;
        public double Segundos;

        public DatosOrientación(int frame, int pasada, int seguimiento, double latitud, double longitud, double altitud, double heading, double segundos)
        {
            NúmeroFrame = frame;
            NúmeroPasada = pasada;
            NúmeroSeguimiento = seguimiento;
            LatitudWgs84 = latitud;
            LongitudWgs84 = longitud;
            Altitud = altitud;
            Heading = heading;
            Segundos = segundos;
        }

        public int Zona
        {
            get
            {
                return (int)(LongitudWgs84 / 6 + 31);
            }
        }

        public int CódigoEpsg
        {
            get
            {
                return (LatitudWgs84 > 0 ? 32600 : 32700) + Zona; 
            }
        }

        public double[] Coordenadas
        {
            get
            {
                return new double[] { LatitudWgs84, LongitudWgs84 };
            }
        }

        public double Kappa
        {
            get
            {
                return Angles.RadianToSexagesimal(Angles.AzimuthToTrigonometric(Angles.SexagesimalToRadian(Heading)));
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Error. No has indicado la ruta del archivo a transformar.");
                Console.Error.WriteLine("El formato es: CCNS4aPatB [archivo de entrada] [archivo de salida]");
                return;
            }

            List<DatosOrientación> orientaciones = CargaOrientaciones(args[0] + ".eo");
            int sistemaCoordenadas = sistemaCoordenadas = SeleccionaCódigoEpsg(orientaciones);
            var transformación = CoordinateTransformationAuthorityFactory.CreateFromCoordinateSystemsCodes(4326, sistemaCoordenadas).MathTransform;

            using (StreamWriter sw = new StreamWriter(args[0]))
            {
                foreach (var orientación in orientaciones)
                {
                    double[] proyectado = transformación.Transform(orientación.Coordenadas);

                    string cadena = string.Format(CultureInfo.InvariantCulture,
                        "{0} {1} {2} {3} {4} {5} {6} {7} 1 1",
                        orientación.NúmeroSeguimiento * 1e7 + orientación.NúmeroPasada * 1e4 + orientación.NúmeroFrame,
                        proyectado[0],
                        proyectado[1],
                        orientación.Altitud,
                        0.0,
                        0.0,
                        orientación.Kappa,
                        orientación.Segundos);

                    sw.WriteLine(cadena);
                }
            }
        }

        private static int SeleccionaCódigoEpsg(List<DatosOrientación> orientaciones)
        {
            int[] códigosEpsg = (from orientación in orientaciones
                                 select orientación.CódigoEpsg).Distinct().ToArray();

            if (códigosEpsg.Length != 1)
            {
                while (true)
                {
                    Console.WriteLine("Se han detectado más de una zona. Por favor, seleccione el sistema de coordenadas en el cual quiere proyectar los centros de proyección\n");

                    for (int i = 0; i < códigosEpsg.Length; i++)
                        Console.WriteLine(string.Format("{0} : {1}", 1 + i, CoordinateSystemAuthorityFactory.CreateCoordinateSystem(códigosEpsg[i]).Name));

                    try
                    {
                        int respuesta = int.Parse(Console.ReadLine());

                        if (respuesta >= 0 && respuesta < códigosEpsg.Length)
                        {
                            return códigosEpsg[respuesta];
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            else
            {
                return códigosEpsg[0];
            }
        }

        private static List<DatosOrientación> CargaOrientaciones(string rutaArchivo)
        {
            List<DatosOrientación> orientaciones = new List<DatosOrientación>();
            DateTime hoy = DateTime.Today;

            using (StreamReader sr = new StreamReader(rutaArchivo))
            {
                LocalizaOrientaciones(sr);
            
                String línea;
                while ((línea = sr.ReadLine()) != null)
                {
                    string[] palabras = línea.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (0 == palabras.Length)
                        continue;

                    int númeroFrame = int.Parse(palabras[1]);
                    int númeroPasada = int.Parse(palabras[6]);
                    int númeroSeguimiento = int.Parse(palabras[7]);

                    bool norte = 'N' == palabras[9][0];
                    double latitudWgs84 = double.Parse(palabras[9].Substring(1), CultureInfo.InvariantCulture);
                    if (!norte)
                        latitudWgs84 *= -1;

                    bool este = 'E' == palabras[10][0];
                    double longitudWgs84 = double.Parse(palabras[10].Substring(1), CultureInfo.InvariantCulture);
                    if (!este)
                        longitudWgs84 *= -1;

                    double altitud = double.Parse(palabras[11], CultureInfo.InvariantCulture);
                    double heading = double.Parse(palabras[13]);

                    int hora = int.Parse(palabras[3].Substring(0,2));
                    int minuto = int.Parse(palabras[3].Substring(2,2));
                    int segundo = int.Parse(palabras[3].Substring(4,2));
                    DateTime tiempo = new DateTime(hoy.Year, hoy.Month, hoy.Day, hora, minuto, segundo);
                    long ticks = tiempo.Ticks - hoy.Ticks;
                    TimeSpan lapso = new TimeSpan(ticks);

                    orientaciones.Add(new DatosOrientación(
                        númeroFrame,
                        númeroPasada,
                        númeroSeguimiento,
                        latitudWgs84,
                        longitudWgs84,
                        altitud,
                        heading, 
                        lapso.TotalSeconds));
                }
            }

            return orientaciones;
        }

        private static void LocalizaOrientaciones(StreamReader sr)
        {
            String línea;

            while ((línea = sr.ReadLine()) != null)
            {
                string[] palabras = línea.Split(new[] { ' ' });
                if (0 == palabras.Length)
                    continue;

                if (palabras[0] == "#Parameter")
                    return;
            }

            throw new Exception("No se han localizado las orientaciones en el archivo proporcionado. Posiblemente no sea un archivo en formato CCNS4 del software Igi.");
        }
    }
}
