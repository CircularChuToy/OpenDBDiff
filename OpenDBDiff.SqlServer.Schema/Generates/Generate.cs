using OpenDBDiff.Abstractions.Schema.Errors;
using OpenDBDiff.Abstractions.Schema.Events;
using OpenDBDiff.Abstractions.Schema.Misc;
using OpenDBDiff.SqlServer.Schema.Compare;
using OpenDBDiff.SqlServer.Schema.Generates.Util;
using OpenDBDiff.SqlServer.Schema.Model;
using OpenDBDiff.SqlServer.Schema.Options;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;

namespace OpenDBDiff.SqlServer.Schema.Generates
{
    public class Generate
    {
        private readonly List<MessageLog> messages;
        private ProgressEventArgs currentlyReading;
        private SynchronizationContext syncContext;

        public Generate()
        {
            messages = new List<MessageLog>();
            OnReading += Generate_OnReading;
            syncContext = SynchronizationContext.Current;
        }

        public static int MaxValue
        {
            get { return Constants.READING_MAX; }
        }

        public string ConnectionString { get; set; }

        private string Name
        {
            get
            {
                string name;
                using (var conn = new SqlConnection(ConnectionString))
                {
                    name = conn.Database;
                }
                return name;
            }
        }

        public SqlOption Options { get; set; }

        private event ProgressEventHandler.ProgressHandler OnReading;

        public event ProgressEventHandler.ProgressHandler OnProgress;

        private void Generate_OnReading(ProgressEventArgs e)
        {
            if (OnProgress != null) OnProgress(e);
        }

        public void RaiseOnReading(ProgressEventArgs e)
        {
            this.currentlyReading = e;

            if (OnReading != null)
            {
                syncContext.Post(state => OnReading((ProgressEventArgs)state), e);
            }
        }

        public void RaiseOnReadingOne(object name)
        {
            if (name != null && this.OnReading != null && this.currentlyReading != null)
            {
                var eOne = new ProgressEventArgs(this.currentlyReading.Message, this.currentlyReading.Progress);
                eOne.Message = eOne.Message.Replace("...", String.Format(": [{0}]", name));
                this.OnReading(eOne);
            }
        }

        /// <summary>
        /// Genera el schema de la base de datos seleccionada y devuelve un objeto Database.
        /// </summary>
        public Database Process()
        {
            string error = "";
            var databaseSchema = new Database();

            //tables.OnTableProgress += new Progress.ProgressHandler(tables_OnTableProgress);
            databaseSchema.Options = Options;
            databaseSchema.Name = Name;
            databaseSchema.Info = (new GenerateDatabase(ConnectionString, Options)).Get(databaseSchema);

            //0
            (new GenerateRules(this)).Fill(databaseSchema, ConnectionString);
            //1 & 2
            (new GenerateTables(this)).Fill(databaseSchema, ConnectionString, messages);
            //3
            (new GenerateUserDataTypes(this)).Fill(databaseSchema, ConnectionString, messages);
            //4
            (new GenerateXMLSchemas(this)).Fill(databaseSchema, ConnectionString);
            //5
            (new GenerateSchemas(this)).Fill(databaseSchema, ConnectionString);
            //6
            (new GenerateUsers(this)).Fill(databaseSchema, ConnectionString);

            //not supported in azure yet
            if (databaseSchema.Info.Version != DatabaseInfo.SQLServerVersion.SQLServerAzure10)
            {
                //7
                (new GeneratePartitionFunctions(this)).Fill(databaseSchema, ConnectionString);
                //8
                (new GeneratePartitionScheme(this)).Fill(databaseSchema, ConnectionString);
                //9
                (new GenerateFileGroups(this)).Fill(databaseSchema, ConnectionString);
            }

            //10
            (new GenerateDDLTriggers(this)).Fill(databaseSchema, ConnectionString);
            //11
            (new GenerateSynonyms(this)).Fill(databaseSchema, ConnectionString);

            //not supported in azure yet
            if (databaseSchema.Info.Version != DatabaseInfo.SQLServerVersion.SQLServerAzure10)
            {
                //12
                (new GenerateAssemblies(this)).Fill(databaseSchema, ConnectionString);
                //
                (new GenerateFullText(this)).Fill(databaseSchema, ConnectionString);
            }

            //13
            (new GenerateStoredProcedures(this)).Fill(databaseSchema, ConnectionString);
            //14
            (new GenerateViews(this)).Fill(databaseSchema, ConnectionString, messages);
            //15
            (new GenerateFunctions(this)).Fill(databaseSchema, ConnectionString);
            //16
            if (Options.Ignore.FilterIndex)
            {
                (new GenerateIndex(this)).Fill(databaseSchema, ConnectionString);
                (new GenerateFullTextIndex(this)).Fill(databaseSchema, ConnectionString);
            }
            //17
            (new GenerateTriggers(this)).Fill(databaseSchema, ConnectionString, messages);
            //18
            (new GenerateTextObjects(this)).Fill(databaseSchema, ConnectionString);

            if (String.IsNullOrEmpty(error))
            {
                /*Las propiedades extendidas deben ir despues de haber capturado el resto de los objetos de la base*/
                (new GenerateExtendedProperties(this)).Fill(databaseSchema, ConnectionString, messages);
                databaseSchema.BuildDependency();
                return databaseSchema;
            }
            else
                throw new SchemaException(error);
        }

        private void tables_OnTableProgress(object sender, ProgressEventArgs e)
        {
            ProgressEventHandler.RaiseOnChange(e);
            OnCompareProgress?.Invoke(e); // Raise the OnCompareProgress event
        }

        // TODO: Static because Compare method is static; static events are not my favorite
        public static event ProgressEventHandler.ProgressHandler OnCompareProgress;

        internal static void RaiseOnCompareProgress(int value, string formatString, params object[] formatParams)
        {
            OnCompareProgress?.Invoke(new ProgressEventArgs(String.Format(formatString, formatParams), value));
        }

        /// <summary>
        /// Generates the differences to migrate a schema from origin to destination
        /// </summary>
        /// <param name="origin">The Origin schema is the schema before our generated actions are applied.</param>
        /// <param name="destination">The Destination schema is the schema after our actions are applied.</param>
        /// <returns></returns>
        public static Database Compare(Database origin, Database destination)
        {
            Database merge = CompareDatabase.GenerateDifferences(origin, destination);
            return merge;
        }
    }
}
