﻿/*
Copyright 2011, 2012 Steve Labbe.

This file is part of Depressurizer.

Depressurizer is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

Depressurizer is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with Depressurizer.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DPLib;

namespace SteamScrape {
    public partial class ScrapeProcDlg : Form {

        object abortLock = new object();

        int threadsToRun = 5;
        int runningThreads;

        Queue<int> jobs;
        GameDB games;

        int totalJobs;
        int jobsCompleted;

        System.DateTime start;

        bool _abort = false;
        bool Aborted {
            get {
                lock( abortLock ) {
                    return _abort;
                }
            }
            set {
                lock( abortLock ) {
                    _abort = value;
                }
            }
        }

        delegate void SimpleDelegate();

        public ScrapeProcDlg( GameDB games, Queue<int> jobs ) {
            InitializeComponent();
            DialogResult = DialogResult.OK;
            this.jobs = jobs;
            this.games = games;

            totalJobs = jobs.Count;

            threadsToRun = Math.Min( threadsToRun, totalJobs );
            jobsCompleted = 0;
        }

        private void UpdateForm_Load( object sender, EventArgs e ) {
            start = DateTime.Now;
            for( int i = 0; i < threadsToRun; i++ ) {
                Thread t = new Thread( new ThreadStart( RunProcess ) );
                t.Start();
                runningThreads++;
            }
            UpdateText();
        }

        private GameDBEntry GetNextGame() {
            int gameId;
            lock( jobs ) {
                if( jobs.Count > 0 ) {
                    gameId = jobs.Dequeue();
                } else {
                    return null;
                }
            }
            GameDBEntry res = null;
            lock( games ) {
                res = games.Games[gameId];
            }
            return res;
        }

        /// <summary>
        /// Runs the next job in the queue, in a thread-safe manner. Aborts ASAP if the form is closed.
        /// </summary>
        /// <returns>True if a job was run, false otherwise</returns>
        private bool RunNextJob() {
            if( Aborted ) return false;

            GameDBEntry game = GetNextGame();
            if( game == null ) {
                return false;
            }
            if( Aborted ) return false;

            string genre = null;
            AppType type = GameDBEntry.ScrapeStore( game.Id, out genre );

            // This lock is critical, as it makes sure that the abort check and the actual game update funtion essentially atomically with reference to form-closing.
            // If this isn't the case, the form could successfully close before this happens, but then it could still go through, and that's no good.
            lock( abortLock ) {
                if( !Aborted ) {
                    game.Type = type;
                    if( type == AppType.Game || type == AppType.DLC || type == AppType.IdRedirect ) {
                        game.Genre = genre;
                    }
                    return true;
                } else {
                    return false;
                }
            }
        }

        private void RunProcess() {
            bool running = true;
            while( !Aborted && running ) {
                running = RunNextJob();
                if( !Aborted && running ) {
                    this.Invoke( new SimpleDelegate( OnJobCompletion ) );
                }
            }
            if( !Aborted ) {
                this.Invoke( new SimpleDelegate( OnThreadEnd ) );
            }
        }

        private void OnJobCompletion() {
            jobsCompleted++;
            UpdateText();
        }

        private void OnThreadEnd() {
            if( !Aborted ) {
                runningThreads--;
                if( runningThreads <= 0 ) {
                    this.Close();
                }
            }
        }

        private void cmdStop_Click( object sender, EventArgs e ) {
            DialogResult = DialogResult.Abort;
            this.Close();
        }

        private void UpdateForm_FormClosing( object sender, FormClosingEventArgs e ) {
            lock( abortLock ) {
                Aborted = true;
            }
        }

        private void UpdateText() {
            double msElapsed = ( DateTime.Now - start ).TotalMilliseconds;
            double msPerItem = msElapsed / (double)jobsCompleted;
            double msRemaining = msPerItem * ( totalJobs - jobsCompleted );
            TimeSpan timeRemaining = TimeSpan.FromMilliseconds( msRemaining );

            StringBuilder sb = new StringBuilder();
            sb.Append( string.Format( "Updating...{0}/{1} complete.", jobsCompleted, totalJobs ) );

            sb.Append( "\nTime Remaining: " );
            if( timeRemaining.TotalMinutes < 1.0 ) {
                sb.Append( "< 1 minute" );
            } else {
                double hours = timeRemaining.TotalHours;
                if( hours >= 1.0 ) {
                    sb.Append( string.Format( "{0:F0}h", hours ) );
                }
                sb.Append( string.Format( "{0:D2}m", timeRemaining.Minutes ) );
            }
            lblText.Text = sb.ToString();
        }
    }
}
