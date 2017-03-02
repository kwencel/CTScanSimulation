﻿using CTScanSimulation.Command;
using CTScanSimulation.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace CTScanSimulation.ViewModel
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private bool canCreateSinogram;
        private bool canRecreateImage;
        private CTScan cTScan;
        private BitmapImage imageWithCT;
        private int loopStep;
        private string orginalImagePath;
        private BitmapImage recreatedImage;
        private BitmapImage sinogram;

        public MainWindowViewModel()
        {
            FilePickerButtonCommand = new RelayCommand(PickFile);
            CreateSinogramButtonCommand = new RelayCommand(CreateSinogram);
            RecreateImageButtonCommand = new RelayCommand(RecreateImage);
            UpdateOrginalImageCommand = new RelayCommand(UpdateOrginalImage);

            EmitterDetectorSystemStep = 1;
            LoopStep = 1;
            NumberOfDetectors = 2;
            EmitterDetectorSystemWidth = 10;

            CanCreateSiogram = false;
            CanRecreateImage = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public bool CanCreateSiogram
        {
            get { return canCreateSinogram; }
            set { canCreateSinogram = value; OnPropertyChanged(nameof(CanCreateSiogram)); }
        }

        public bool CanRecreateImage
        {
            get { return canRecreateImage; }
            set { canRecreateImage = value; OnPropertyChanged(nameof(CanRecreateImage)); }
        }

        public ICommand CreateSinogramButtonCommand { get; set; }
        public int EmitterDetectorSystemStep { get; set; }
        public int EmitterDetectorSystemWidth { get; set; }
        public ICommand FilePickerButtonCommand { get; set; }

        public BitmapImage ImageWithCT
        {
            get { return imageWithCT; }
            set { imageWithCT = value; OnPropertyChanged(nameof(ImageWithCT)); }
        }

        public int LoopStep { get; set; }
        public int NumberOfDetectors { get; set; }

        public string OrginalImagePath
        {
            get { return orginalImagePath; }
            set { orginalImagePath = value; OnPropertyChanged(nameof(OrginalImagePath)); }
        }

        public BitmapImage RecreatedImage
        {
            get { return recreatedImage; }
            set { recreatedImage = value; OnPropertyChanged(nameof(recreatedImage)); }
        }

        public ICommand RecreateImageButtonCommand { get; set; }

        public BitmapImage Sinogram
        {
            get { return sinogram; }
            set { sinogram = value; OnPropertyChanged(nameof(sinogram)); }
        }

        public ICommand UpdateOrginalImageCommand { get; set; }

        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }

        private BitmapImage BitmapToBitmapImage(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Png);
                memory.Position = 0;
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmap.Dispose();
                return bitmapImage;
            }
        }

        private void CreateSinogram(object obj)
        {
            try
            {
                Sinogram = BitmapToBitmapImage(cTScan.CreateSinogram());
                CanRecreateImage = true;
            }
            catch (FileNotFoundException ex)
            {
                MessageBox.Show(ex.Message, "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PickFile(object obj)
        {
            // Create the OpenFIleDialog object
            var openPicker = new Microsoft.Win32.OpenFileDialog
            {
                // Add file filters
                DefaultExt = ".png",
                Filter = "Obrazy |*.png;*.jpg;*.jpeg;*.bmp"
            };

            // Display the OpenFileDialog by calling ShowDialog method
            bool? result = openPicker.ShowDialog();

            // Check to see if we have a result
            if (result == true)
            {
                // Application now has read/write access to the picked file
                // I am saving the file path to a textbox in the UI to display to the user
                OrginalImagePath = openPicker.FileName.ToString();
                CanCreateSiogram = true;
                var orginalImage = new Bitmap(orginalImagePath);
                cTScan = new CTScan(orginalImage, EmitterDetectorSystemStep, NumberOfDetectors, EmitterDetectorSystemWidth);
                UpdateOrginalImage(null);
            }
        }

        private void RecreateImage(object obj)
        {
            this.RecreatedImage = BitmapToBitmapImage(cTScan.RecreateImage());
        }

        private void UpdateOrginalImage(object obj)
        {
            if (cTScan != null)
            {
                ImageWithCT = BitmapToBitmapImage(cTScan.DrawCTSystem(LoopStep));
            }
        }
    }
}