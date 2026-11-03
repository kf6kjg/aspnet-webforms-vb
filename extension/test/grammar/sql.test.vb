' SYNTAX TEST "source.asp.vb.net" "SQL in VB strings: SQLCmd statements and ' lang= markers (ADR 0010)"
Partial Class Admin
 Sub Load()
  SQLCmd = "Select Name, Concat(First,' ',Last) as FullName " +
' ^^^^^^ meta.sql-statement.vb variable.other.asp
'           ^^^^^^ string.quoted.double.asp meta.sql-fragment.vb keyword.other.sql
'                        ^^^^^^ support.function.sql
'                                     ^^^ string.quoted.single.sql
'                                               ^^ keyword.other.sql
'                                                  ^^^^^^^^ meta.sql-fragment.vb - keyword
'                                                             ^ meta.sql-statement.vb - string
           "From control " +
'           ^^^^ meta.sql-statement.vb keyword.other.sql
           "Where Control='ADMINSYSTEM' and Name=" + MyDB.SQLStr(NName.Text) +
'                         ^^^^^^^^^^^^^ string.quoted.single.sql
'                                       ^^^ keyword.other.sql
'                                                    ^^^^ meta.sql-statement.vb - string
           "Name='" + x + "' Order by SortOrder Limit 10"
'                ^ string.quoted.double.asp - string.quoted.single.sql
'                     ^ meta.sql-statement.vb - string
'                            ^^^^^ keyword.other.sql
'                                                     ^^ constant.numeric.sql
  Dim dr = MyDB.GetReader(Session("DBConnection"), SQLCmd)
' ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ - meta.sql-statement.vb
  tMsg = "Update users Set SQLCmd: " + SQLCmd
'         ^^^^^^ - keyword.other.sql
  SQLCmd = "Select Name From users" ' a comment ends the statement
'                                   ^ comment.line.apostrophe.asp - meta.sql-statement.vb
  x = 1
' ^^^^^ - meta.sql-statement.vb
  ' lang=tsql
' ^^^^^^^^^^^ meta.sql-statement.vb comment.line.apostrophe.asp
'   ^^^^^^^^^ keyword.other.language-marker.vb
  tSel = "Select Top 5 [Name], @@Identity From users Where Name=@Name" +
'                ^^^ keyword.other.sql
'                      ^^^^^^ variable.other.quoted.bracket.sql
'                              ^^^^^^^^^^ variable.language.sql
'                                                               ^^^^^ variable.parameter.sql
         " -- note"
'          ^^^^^^^ comment.line.double-dash.sql
  ' lang=mysql
  SQLCmd = "Select `Name` From #temp" ' the marker wins over the SQLCmd default
'                  ^^^^^^ variable.other.quoted.backtick.sql
'                              ^^^^^ comment.line.number-sign.sql
  SQLCmd = "Select [Name] From #temp Where a=""b"""
'                  ^^^^^^ variable.other.quoted.bracket.sql
'                              ^^^^^ meta.sql-fragment.vb - comment
'                                            ^^ constant.character.escape.apostrophe.asp
'                                                 ^ punctuation.definition.string.end.asp
  ' language = PostgreSQL
  tSel = "Select a::int From b Where c=$1 Returning d"
'                 ^^ keyword.operator.cast.sql
'                                      ^^ variable.parameter.sql
'                                         ^^^^^^^^^ keyword.other.sql
  ' lang=sql

  tAfterBlank = "Select a From b"
' ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ - meta.sql-statement.vb
  If SQLCmd = "" Then tOther = "Select 1"
'                              ^^^^^^^^^^ - meta.sql-statement.vb
 End Sub
End Class
